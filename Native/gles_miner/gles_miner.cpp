// Backend OpenGL ES 3.1 (compute shader) para aparelhos sem Vulkan.
// Mesmo algoritmo do backend Vulkan (../vulkan_miner/shaders/sha256d.comp).
#include "gles_miner.h"

#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl31.h>

#include <cstring>
#include <mutex>
#include <string>

#include "../vulkan_miner/sha256_cpu.h"

#ifndef EGL_OPENGL_ES3_BIT
#define EGL_OPENGL_ES3_BIT 0x00000040
#endif

namespace {

constexpr uint32_t kLocalSize = 64;   // igual a local_size_x no shader
constexpr uint32_t kPerThread = 4;    // igual a PER_THREAD no shader
constexpr uint32_t kMaxResults = 32;  // igual a MAX_RESULTS no shader

struct ResultBuffer {
    uint32_t count;
    uint32_t nonces[kMaxResults];
};

const char* kShaderSource = R"GLSL(#version 310 es
precision highp int;
layout(local_size_x = 64) in;

#define PER_THREAD 4u
#define MAX_RESULTS 32u

uniform uvec4 uMid0;  // midstate[0..3]
uniform uvec4 uMid1;  // midstate[4..7]
uniform uvec4 uTail;  // w0, w1, w2 (bytes 64..75 do cabecalho), nao usado
uniform uvec4 uCfg;   // startNonce, count, targetTop, nao usado

layout(std430, binding = 0) buffer Results {
    uint count;
    uint nonces[MAX_RESULTS];
} res;

const uint K[64] = uint[64](
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
    0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
    0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
    0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
    0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
    0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u);

uint rotr(uint x, uint n) { return (x >> n) | (x << (32u - n)); }

uint bswap32(uint x) {
    return (x >> 24) | ((x >> 8) & 0x0000ff00u) | ((x << 8) & 0x00ff0000u) | (x << 24);
}

void compress(inout uint st[8], uint w[16]) {
    uint a = st[0], b = st[1], c = st[2], d = st[3];
    uint e = st[4], f = st[5], g = st[6], h = st[7];
    for (uint i = 0u; i < 64u; i++) {
        uint wi;
        if (i < 16u) {
            wi = w[i];
        } else {
            uint w15 = w[(i + 1u) & 15u];
            uint w2  = w[(i + 14u) & 15u];
            uint s0 = rotr(w15, 7u) ^ rotr(w15, 18u) ^ (w15 >> 3u);
            uint s1 = rotr(w2, 17u) ^ rotr(w2, 19u) ^ (w2 >> 10u);
            wi = w[i & 15u] + s0 + w[(i + 9u) & 15u] + s1;
            w[i & 15u] = wi;
        }
        uint S1 = rotr(e, 6u) ^ rotr(e, 11u) ^ rotr(e, 25u);
        uint ch = (e & f) ^ (~e & g);
        uint t1 = h + S1 + ch + K[i] + wi;
        uint S0 = rotr(a, 2u) ^ rotr(a, 13u) ^ rotr(a, 22u);
        uint maj = (a & b) ^ (a & c) ^ (b & c);
        uint t2 = S0 + maj;
        h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
    }
    st[0] += a; st[1] += b; st[2] += c; st[3] += d;
    st[4] += e; st[5] += f; st[6] += g; st[7] += h;
}

void main() {
    uint base = gl_GlobalInvocationID.x * PER_THREAD;
    uint startNonce = uCfg.x;
    uint total = uCfg.y;
    uint targetTop = uCfg.z;

    for (uint k = 0u; k < PER_THREAD; k++) {
        uint idx = base + k;
        if (idx >= total) break;
        uint nonce = startNonce + idx;

        uint st[8] = uint[8](uMid0.x, uMid0.y, uMid0.z, uMid0.w,
                             uMid1.x, uMid1.y, uMid1.z, uMid1.w);
        uint w[16] = uint[16](uTail.x, uTail.y, uTail.z, bswap32(nonce),
                              0x80000000u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 640u);
        compress(st, w);

        uint st2[8] = uint[8](0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au,
                              0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u);
        uint w2[16] = uint[16](st[0], st[1], st[2], st[3], st[4], st[5], st[6], st[7],
                               0x80000000u, 0u, 0u, 0u, 0u, 0u, 0u, 256u);
        compress(st2, w2);

        if (bswap32(st2[7]) <= targetTop) {
            uint pos = atomicAdd(res.count, 1u);
            if (pos < MAX_RESULTS) res.nonces[pos] = nonce;
        }
    }
}
)GLSL";

struct Context {
    bool ready = false;
    EGLDisplay display = EGL_NO_DISPLAY;
    EGLContext context = EGL_NO_CONTEXT;
    EGLSurface surface = EGL_NO_SURFACE;
    GLuint program = 0;
    GLuint buffer = 0;
    GLint locMid0 = -1, locMid1 = -1, locTail = -1, locCfg = -1;
    GLint maxGroups = 0;
    std::string renderer;
};

Context g;
std::mutex g_mutex;
std::string g_error;

int fail(const std::string& what, int code) {
    g_error = what;
    return code;
}

// O contexto OpenGL só pode estar ativo em uma thread por vez. O C# pode trocar
// de thread entre lotes, então ativamos no começo de cada chamada e soltamos no fim.
bool bind_current() {
    return eglMakeCurrent(g.display, g.surface, g.surface, g.context) == EGL_TRUE;
}

void release_current() {
    eglMakeCurrent(g.display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
}

void destroy_all() {
    if (g.display != EGL_NO_DISPLAY && g.context != EGL_NO_CONTEXT) {
        if (bind_current()) {
            if (g.buffer) glDeleteBuffers(1, &g.buffer);
            if (g.program) glDeleteProgram(g.program);
        }
        release_current();
    }
    if (g.display != EGL_NO_DISPLAY) {
        if (g.context != EGL_NO_CONTEXT) eglDestroyContext(g.display, g.context);
        if (g.surface != EGL_NO_SURFACE) eglDestroySurface(g.display, g.surface);
        eglTerminate(g.display);
    }
    g = Context();
}

int init_locked() {
    if (g.ready) return 0;

    g.display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (g.display == EGL_NO_DISPLAY) return fail("eglGetDisplay falhou", -1);
    EGLint major = 0, minor = 0;
    if (!eglInitialize(g.display, &major, &minor)) {
        g.display = EGL_NO_DISPLAY;
        return fail("eglInitialize falhou", -2);
    }

    const EGLint configAttribs[] = {EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT,
                                    EGL_SURFACE_TYPE, EGL_PBUFFER_BIT, EGL_NONE};
    EGLConfig config = nullptr;
    EGLint numConfigs = 0;
    if (!eglChooseConfig(g.display, configAttribs, &config, 1, &numConfigs) || numConfigs < 1) {
        destroy_all();
        return fail("Sem configuração EGL para OpenGL ES 3", -3);
    }

    const EGLint pbufferAttribs[] = {EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE};
    g.surface = eglCreatePbufferSurface(g.display, config, pbufferAttribs);
    if (g.surface == EGL_NO_SURFACE) {
        destroy_all();
        return fail("eglCreatePbufferSurface falhou", -4);
    }

    const EGLint contextAttribs[] = {EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE};
    g.context = eglCreateContext(g.display, config, EGL_NO_CONTEXT, contextAttribs);
    if (g.context == EGL_NO_CONTEXT) {
        destroy_all();
        return fail("eglCreateContext (OpenGL ES 3) falhou", -5);
    }

    if (!bind_current()) {
        destroy_all();
        return fail("eglMakeCurrent falhou", -6);
    }

    GLint glMajor = 0, glMinor = 0;
    glGetIntegerv(GL_MAJOR_VERSION, &glMajor);
    glGetIntegerv(GL_MINOR_VERSION, &glMinor);
    const char* renderer = reinterpret_cast<const char*>(glGetString(GL_RENDERER));
    g.renderer = renderer ? renderer : "GPU desconhecida";
    if (glMajor < 3 || (glMajor == 3 && glMinor < 1)) {
        std::string msg = "OpenGL ES 3.1 é necessário para compute shader; este aparelho tem " +
                          std::to_string(glMajor) + "." + std::to_string(glMinor);
        destroy_all();
        return fail(msg, -7);
    }

    glGetIntegeri_v(GL_MAX_COMPUTE_WORK_GROUP_COUNT, 0, &g.maxGroups);

    GLuint shader = glCreateShader(GL_COMPUTE_SHADER);
    glShaderSource(shader, 1, &kShaderSource, nullptr);
    glCompileShader(shader);
    GLint ok = 0;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
    if (!ok) {
        char log[512] = {0};
        glGetShaderInfoLog(shader, sizeof(log) - 1, nullptr, log);
        glDeleteShader(shader);
        destroy_all();
        return fail(std::string("Shader recusado pelo driver: ") + log, -8);
    }

    g.program = glCreateProgram();
    glAttachShader(g.program, shader);
    glLinkProgram(g.program);
    glDeleteShader(shader);
    glGetProgramiv(g.program, GL_LINK_STATUS, &ok);
    if (!ok) {
        char log[512] = {0};
        glGetProgramInfoLog(g.program, sizeof(log) - 1, nullptr, log);
        destroy_all();
        return fail(std::string("Link do programa falhou: ") + log, -9);
    }

    g.locMid0 = glGetUniformLocation(g.program, "uMid0");
    g.locMid1 = glGetUniformLocation(g.program, "uMid1");
    g.locTail = glGetUniformLocation(g.program, "uTail");
    g.locCfg = glGetUniformLocation(g.program, "uCfg");
    if (g.locMid0 < 0 || g.locMid1 < 0 || g.locTail < 0 || g.locCfg < 0) {
        destroy_all();
        return fail("Uniforms do shader não encontrados", -10);
    }

    glGenBuffers(1, &g.buffer);
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, g.buffer);
    glBufferData(GL_SHADER_STORAGE_BUFFER, sizeof(ResultBuffer), nullptr, GL_DYNAMIC_READ);
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, 0);
    if (glGetError() != GL_NO_ERROR) {
        destroy_all();
        return fail("Falha ao criar o buffer de resultados", -11);
    }

    release_current();
    g.ready = true;
    return 0;
}

int scan_locked(const uint8_t* header, uint32_t startNonce, uint32_t count, uint32_t targetTop,
                uint32_t* out, uint32_t maxOut, uint32_t* found) {
    if (!g.ready) return fail("Backend OpenGL ES não inicializado", -20);
    if (!header || !found) return fail("Argumentos inválidos", -21);
    *found = 0;
    if (count == 0) return 0;

    uint64_t groups64 = (uint64_t(count) + kLocalSize * kPerThread - 1) / (kLocalSize * kPerThread);
    if (g.maxGroups > 0 && groups64 > uint64_t(g.maxGroups))
        return fail("Lote maior que o limite de workgroups da GPU", -22);

    vkm::JobConstants job = vkm::prepare_job(header);

    if (!bind_current()) return fail("eglMakeCurrent falhou no lote", -23);

    glUseProgram(g.program);
    glUniform4ui(g.locMid0, job.midstate[0], job.midstate[1], job.midstate[2], job.midstate[3]);
    glUniform4ui(g.locMid1, job.midstate[4], job.midstate[5], job.midstate[6], job.midstate[7]);
    glUniform4ui(g.locTail, job.tail[0], job.tail[1], job.tail[2], 0u);
    glUniform4ui(g.locCfg, startNonce, count, targetTop, 0u);

    glBindBuffer(GL_SHADER_STORAGE_BUFFER, g.buffer);
    const uint32_t zero = 0;
    glBufferSubData(GL_SHADER_STORAGE_BUFFER, 0, sizeof(zero), &zero);
    glBindBufferBase(GL_SHADER_STORAGE_BUFFER, 0, g.buffer);

    glDispatchCompute(GLuint(groups64), 1, 1);
    glMemoryBarrier(GL_BUFFER_UPDATE_BARRIER_BIT | GL_SHADER_STORAGE_BARRIER_BIT);
    glFinish();

    int rc = 0;
    const void* mapped = glMapBufferRange(GL_SHADER_STORAGE_BUFFER, 0, sizeof(ResultBuffer), GL_MAP_READ_BIT);
    if (mapped) {
        ResultBuffer result;
        std::memcpy(&result, mapped, sizeof(result));
        glUnmapBuffer(GL_SHADER_STORAGE_BUFFER);

        uint32_t n = result.count;
        if (n > kMaxResults) n = kMaxResults;
        if (n > maxOut) n = maxOut;
        for (uint32_t i = 0; i < n; i++) out[i] = result.nonces[i];
        *found = n;
    } else {
        rc = fail("glMapBufferRange falhou", -24);
    }

    GLenum err = glGetError();
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, 0);
    release_current();
    if (rc == 0 && err != GL_NO_ERROR)
        rc = fail("Erro OpenGL ES " + std::to_string(int(err)) + " durante o lote", -25);
    return rc;
}

}  // namespace

extern "C" {

int glm_init(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    return init_locked();
}

void glm_shutdown(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    destroy_all();
}

const char* glm_device_name(void) { return g.renderer.c_str(); }
const char* glm_last_error(void) { return g_error.c_str(); }

int glm_scan(const uint8_t* header80, uint32_t start_nonce, uint32_t count, uint32_t target_top,
             uint32_t* out_nonces, uint32_t max_out, uint32_t* out_found) {
    std::lock_guard<std::mutex> lock(g_mutex);
    return scan_locked(header80, start_nonce, count, target_top, out_nonces, max_out, out_found);
}

int glm_self_test(void) {
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
    int rc = scan_locked(genesis, goodNonce - 2048u, 4096u, 0u, found, 8, &nFound);
    if (rc != 0) return rc;
    if (nFound != 1 || found[0] != goodNonce)
        return fail("Auto-teste: a GPU não reproduziu o hash do bloco gênese", -31);
    return 0;
}

}  // extern "C"
