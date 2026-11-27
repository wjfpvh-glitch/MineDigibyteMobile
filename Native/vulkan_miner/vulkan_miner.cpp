#include "vulkan_miner.h"

#include <vulkan/vulkan.h>

#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#include "sha256_cpu.h"

// SPIR-V gerado pelo CMake a partir de shaders/sha256d.comp.
static const uint32_t kSpirv[] = {
#include "sha256d.spv.inc"
};

namespace {

constexpr uint32_t kLocalSize = 64;   // igual a local_size_x no shader
constexpr uint32_t kPerThread = 4;    // igual a PER_THREAD no shader
constexpr uint32_t kMaxResults = 32;  // igual a MAX_RESULTS no shader

struct PushConstants {
    uint32_t mid[8];
    uint32_t tail[4];
    uint32_t cfg[4];
};  // 64 bytes (mínimo garantido pelo Vulkan: 128)

struct ResultBuffer {
    uint32_t count;
    uint32_t nonces[kMaxResults];
};

struct Context {
    bool ready = false;
    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice phys = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    VkQueue queue = VK_NULL_HANDLE;
    uint32_t queueFamily = 0;
    VkShaderModule shader = VK_NULL_HANDLE;
    VkDescriptorSetLayout setLayout = VK_NULL_HANDLE;
    VkPipelineLayout pipelineLayout = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
    VkDescriptorPool pool = VK_NULL_HANDLE;
    VkDescriptorSet set = VK_NULL_HANDLE;
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    ResultBuffer* mapped = nullptr;
    VkCommandPool cmdPool = VK_NULL_HANDLE;
    VkCommandBuffer cmd = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    uint32_t maxGroups = 0;
    std::string deviceName;
};

Context g;
std::mutex g_mutex;
std::string g_error;

int fail(const char* what, int code, VkResult r = VK_SUCCESS) {
    g_error = what;
    if (r != VK_SUCCESS) g_error += " (VkResult " + std::to_string(int(r)) + ")";
    return code;
}

void destroy_all() {
    if (g.device) vkDeviceWaitIdle(g.device);
    if (g.fence) vkDestroyFence(g.device, g.fence, nullptr);
    if (g.cmdPool) vkDestroyCommandPool(g.device, g.cmdPool, nullptr);
    if (g.mapped) vkUnmapMemory(g.device, g.memory);
    if (g.buffer) vkDestroyBuffer(g.device, g.buffer, nullptr);
    if (g.memory) vkFreeMemory(g.device, g.memory, nullptr);
    if (g.pool) vkDestroyDescriptorPool(g.device, g.pool, nullptr);
    if (g.pipeline) vkDestroyPipeline(g.device, g.pipeline, nullptr);
    if (g.pipelineLayout) vkDestroyPipelineLayout(g.device, g.pipelineLayout, nullptr);
    if (g.setLayout) vkDestroyDescriptorSetLayout(g.device, g.setLayout, nullptr);
    if (g.shader) vkDestroyShaderModule(g.device, g.shader, nullptr);
    if (g.device) vkDestroyDevice(g.device, nullptr);
    if (g.instance) vkDestroyInstance(g.instance, nullptr);
    g = Context();
}

int init_locked() {
    if (g.ready) return 0;

    VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO};
    app.pApplicationName = "MineDigibyteMobile";
    app.apiVersion = VK_API_VERSION_1_0;
    VkInstanceCreateInfo ici{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
    ici.pApplicationInfo = &app;
    VkResult r = vkCreateInstance(&ici, nullptr, &g.instance);
    if (r != VK_SUCCESS) return fail("vkCreateInstance falhou", -1, r);

    uint32_t n = 0;
    vkEnumeratePhysicalDevices(g.instance, &n, nullptr);
    if (n == 0) { destroy_all(); return fail("Nenhum dispositivo Vulkan", -2); }
    std::vector<VkPhysicalDevice> devs(n);
    vkEnumeratePhysicalDevices(g.instance, &n, devs.data());

    // Prefere GPU discreta > integrada > outras, desde que tenha fila de compute.
    int bestScore = -1;
    for (VkPhysicalDevice d : devs) {
        VkPhysicalDeviceProperties props;
        vkGetPhysicalDeviceProperties(d, &props);
        uint32_t qn = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(d, &qn, nullptr);
        std::vector<VkQueueFamilyProperties> qs(qn);
        vkGetPhysicalDeviceQueueFamilyProperties(d, &qn, qs.data());
        for (uint32_t i = 0; i < qn; i++) {
            if (!(qs[i].queueFlags & VK_QUEUE_COMPUTE_BIT)) continue;
            int score = props.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU ? 3
                      : props.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU ? 2 : 1;
            if (score > bestScore) {
                bestScore = score;
                g.phys = d;
                g.queueFamily = i;
                g.deviceName = props.deviceName;
                g.maxGroups = props.limits.maxComputeWorkGroupCount[0];
            }
            break;
        }
    }
    if (!g.phys) { destroy_all(); return fail("Nenhuma fila de compute encontrada", -3); }

    float prio = 1.0f;
    VkDeviceQueueCreateInfo qci{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
    qci.queueFamilyIndex = g.queueFamily;
    qci.queueCount = 1;
    qci.pQueuePriorities = &prio;
    VkDeviceCreateInfo dci{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    dci.queueCreateInfoCount = 1;
    dci.pQueueCreateInfos = &qci;
    r = vkCreateDevice(g.phys, &dci, nullptr, &g.device);
    if (r != VK_SUCCESS) { destroy_all(); return fail("vkCreateDevice falhou", -4, r); }
    vkGetDeviceQueue(g.device, g.queueFamily, 0, &g.queue);

    VkShaderModuleCreateInfo smci{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};
    smci.codeSize = sizeof(kSpirv);
    smci.pCode = kSpirv;
    r = vkCreateShaderModule(g.device, &smci, nullptr, &g.shader);
    if (r != VK_SUCCESS) { destroy_all(); return fail("Shader SPIR-V rejeitado pelo driver", -5, r); }

    VkDescriptorSetLayoutBinding bind{};
    bind.binding = 0;
    bind.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bind.descriptorCount = 1;
    bind.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    VkDescriptorSetLayoutCreateInfo dslci{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
    dslci.bindingCount = 1;
    dslci.pBindings = &bind;
    r = vkCreateDescriptorSetLayout(g.device, &dslci, nullptr, &g.setLayout);
    if (r != VK_SUCCESS) { destroy_all(); return fail("DescriptorSetLayout falhou", -6, r); }

    VkPushConstantRange pcr{VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(PushConstants)};
    VkPipelineLayoutCreateInfo plci{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};
    plci.setLayoutCount = 1;
    plci.pSetLayouts = &g.setLayout;
    plci.pushConstantRangeCount = 1;
    plci.pPushConstantRanges = &pcr;
    r = vkCreatePipelineLayout(g.device, &plci, nullptr, &g.pipelineLayout);
    if (r != VK_SUCCESS) { destroy_all(); return fail("PipelineLayout falhou", -7, r); }

    VkComputePipelineCreateInfo cpci{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};
    cpci.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    cpci.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
    cpci.stage.module = g.shader;
    cpci.stage.pName = "main";
    cpci.layout = g.pipelineLayout;
    r = vkCreateComputePipelines(g.device, VK_NULL_HANDLE, 1, &cpci, nullptr, &g.pipeline);
    if (r != VK_SUCCESS) { destroy_all(); return fail("Pipeline de compute falhou", -8, r); }

    // Buffer de resultados visível ao host (memória unificada nos celulares).
    VkBufferCreateInfo bci{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
    bci.size = sizeof(ResultBuffer);
    bci.usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
    bci.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    r = vkCreateBuffer(g.device, &bci, nullptr, &g.buffer);
    if (r != VK_SUCCESS) { destroy_all(); return fail("vkCreateBuffer falhou", -9, r); }

    VkMemoryRequirements req;
    vkGetBufferMemoryRequirements(g.device, g.buffer, &req);
    VkPhysicalDeviceMemoryProperties mp;
    vkGetPhysicalDeviceMemoryProperties(g.phys, &mp);
    const VkMemoryPropertyFlags want =
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
    uint32_t memType = UINT32_MAX;
    for (uint32_t i = 0; i < mp.memoryTypeCount; i++) {
        if ((req.memoryTypeBits & (1u << i)) && (mp.memoryTypes[i].propertyFlags & want) == want) {
            memType = i;
            break;
        }
    }
    if (memType == UINT32_MAX) { destroy_all(); return fail("Sem memória host-visible/coherent", -10); }
    VkMemoryAllocateInfo mai{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
    mai.allocationSize = req.size;
    mai.memoryTypeIndex = memType;
    r = vkAllocateMemory(g.device, &mai, nullptr, &g.memory);
    if (r != VK_SUCCESS) { destroy_all(); return fail("vkAllocateMemory falhou", -11, r); }
    vkBindBufferMemory(g.device, g.buffer, g.memory, 0);
    void* ptr = nullptr;
    r = vkMapMemory(g.device, g.memory, 0, VK_WHOLE_SIZE, 0, &ptr);
    if (r != VK_SUCCESS) { destroy_all(); return fail("vkMapMemory falhou", -12, r); }
    g.mapped = static_cast<ResultBuffer*>(ptr);

    VkDescriptorPoolSize ps{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1};
    VkDescriptorPoolCreateInfo dpci{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};
    dpci.maxSets = 1;
    dpci.poolSizeCount = 1;
    dpci.pPoolSizes = &ps;
    r = vkCreateDescriptorPool(g.device, &dpci, nullptr, &g.pool);
    if (r != VK_SUCCESS) { destroy_all(); return fail("DescriptorPool falhou", -13, r); }
    VkDescriptorSetAllocateInfo dsai{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};
    dsai.descriptorPool = g.pool;
    dsai.descriptorSetCount = 1;
    dsai.pSetLayouts = &g.setLayout;
    r = vkAllocateDescriptorSets(g.device, &dsai, &g.set);
    if (r != VK_SUCCESS) { destroy_all(); return fail("DescriptorSet falhou", -14, r); }
    VkDescriptorBufferInfo dbi{g.buffer, 0, VK_WHOLE_SIZE};
    VkWriteDescriptorSet wds{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};
    wds.dstSet = g.set;
    wds.dstBinding = 0;
    wds.descriptorCount = 1;
    wds.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    wds.pBufferInfo = &dbi;
    vkUpdateDescriptorSets(g.device, 1, &wds, 0, nullptr);

    VkCommandPoolCreateInfo cpi{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
    cpi.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    cpi.queueFamilyIndex = g.queueFamily;
    r = vkCreateCommandPool(g.device, &cpi, nullptr, &g.cmdPool);
    if (r != VK_SUCCESS) { destroy_all(); return fail("CommandPool falhou", -15, r); }
    VkCommandBufferAllocateInfo cbai{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
    cbai.commandPool = g.cmdPool;
    cbai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    cbai.commandBufferCount = 1;
    r = vkAllocateCommandBuffers(g.device, &cbai, &g.cmd);
    if (r != VK_SUCCESS) { destroy_all(); return fail("CommandBuffer falhou", -16, r); }
    VkFenceCreateInfo fci{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
    r = vkCreateFence(g.device, &fci, nullptr, &g.fence);
    if (r != VK_SUCCESS) { destroy_all(); return fail("Fence falhou", -17, r); }

    g.ready = true;
    return 0;
}

int scan_locked(const uint8_t* header, uint32_t startNonce, uint32_t count, uint32_t targetTop,
                uint32_t* out, uint32_t maxOut, uint32_t* found) {
    if (!g.ready) return fail("Backend Vulkan não inicializado", -20);
    if (!header || !found) return fail("Argumentos inválidos", -21);
    *found = 0;
    if (count == 0) return 0;

    uint64_t groups64 = (uint64_t(count) + kLocalSize * kPerThread - 1) / (kLocalSize * kPerThread);
    if (groups64 > g.maxGroups) return fail("Lote maior que o limite de workgroups da GPU", -22);

    vkm::JobConstants job = vkm::prepare_job(header);
    PushConstants pc{};
    std::memcpy(pc.mid, job.midstate, sizeof(pc.mid));
    pc.tail[0] = job.tail[0];
    pc.tail[1] = job.tail[1];
    pc.tail[2] = job.tail[2];
    pc.cfg[0] = startNonce;
    pc.cfg[1] = count;
    pc.cfg[2] = targetTop;

    g.mapped->count = 0;  // a GPU está parada: o fence anterior já foi aguardado

    VkCommandBufferBeginInfo bi{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    vkResetCommandBuffer(g.cmd, 0);
    vkBeginCommandBuffer(g.cmd, &bi);
    vkCmdBindPipeline(g.cmd, VK_PIPELINE_BIND_POINT_COMPUTE, g.pipeline);
    vkCmdBindDescriptorSets(g.cmd, VK_PIPELINE_BIND_POINT_COMPUTE, g.pipelineLayout, 0, 1, &g.set, 0, nullptr);
    vkCmdPushConstants(g.cmd, g.pipelineLayout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(pc), &pc);
    vkCmdDispatch(g.cmd, uint32_t(groups64), 1, 1);
    VkResult r = vkEndCommandBuffer(g.cmd);
    if (r != VK_SUCCESS) return fail("vkEndCommandBuffer falhou", -23, r);

    VkSubmitInfo si{VK_STRUCTURE_TYPE_SUBMIT_INFO};
    si.commandBufferCount = 1;
    si.pCommandBuffers = &g.cmd;
    r = vkQueueSubmit(g.queue, 1, &si, g.fence);
    if (r != VK_SUCCESS) return fail("vkQueueSubmit falhou", -24, r);

    r = vkWaitForFences(g.device, 1, &g.fence, VK_TRUE, 10ull * 1000 * 1000 * 1000);  // 10 s
    vkResetFences(g.device, 1, &g.fence);
    if (r != VK_SUCCESS) return fail("GPU não terminou o lote a tempo", -25, r);

    uint32_t n = g.mapped->count;
    if (n > kMaxResults) n = kMaxResults;
    if (n > maxOut) n = maxOut;
    for (uint32_t i = 0; i < n; i++) out[i] = g.mapped->nonces[i];
    *found = n;
    return 0;
}

}  // namespace

extern "C" {

int vkm_init(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    return init_locked();
}

void vkm_shutdown(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    destroy_all();
}

const char* vkm_device_name(void) { return g.deviceName.c_str(); }
const char* vkm_last_error(void) { return g_error.c_str(); }

int vkm_scan(const uint8_t* header80, uint32_t start_nonce, uint32_t count, uint32_t target_top,
             uint32_t* out_nonces, uint32_t max_out, uint32_t* out_found) {
    std::lock_guard<std::mutex> lock(g_mutex);
    return scan_locked(header80, start_nonce, count, target_top, out_nonces, max_out, out_found);
}

int vkm_self_test(void) {
    // Cabeçalho do bloco gênese; o nonce válido é 2083236893 (0x7c2bac1d).
    static const uint8_t genesis[80] = {
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x3b, 0xa3, 0xed, 0xfd, 0x7a, 0x7b, 0x12, 0xb2, 0x7a, 0xc7, 0x2c, 0x3e,
        0x67, 0x76, 0x8f, 0x61, 0x7f, 0xc8, 0x1b, 0xc3, 0x88, 0x8a, 0x51, 0x32, 0x3a, 0x9f, 0xb8, 0xaa,
        0x4b, 0x1e, 0x5e, 0x4a, 0x29, 0xab, 0x5f, 0x49, 0xff, 0xff, 0x00, 0x1d, 0x1d, 0xac, 0x2b, 0x7c};
    const uint32_t goodNonce = 2083236893u;

    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g.ready) return fail("Auto-teste sem init", -30);

    uint32_t found[8] = {0};
    uint32_t nFound = 0;
    // 4096 nonces ao redor do correto. O gênese é o único com 32 bits de topo = 0.
    int rc = scan_locked(genesis, goodNonce - 2048u, 4096u, 0u, found, 8, &nFound);
    if (rc != 0) return rc;
    if (nFound != 1 || found[0] != goodNonce)
        return fail("Auto-teste: a GPU não reproduziu o hash do bloco gênese", -31);
    return 0;
}

}  // extern "C"
