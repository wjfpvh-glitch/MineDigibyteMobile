#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define GLM_EXPORT __attribute__((visibility("default")))

// 0 = ok; negativo = erro (veja glm_last_error()).
GLM_EXPORT int glm_init(void);
GLM_EXPORT void glm_shutdown(void);
GLM_EXPORT const char* glm_device_name(void);
GLM_EXPORT const char* glm_last_error(void);

// Igual a vkm_scan do backend Vulkan: devolve nonces cujo hash tem os 32 bits
// mais significativos <= target_top. O chamador confirma com o alvo completo.
GLM_EXPORT int glm_scan(const uint8_t* header80, uint32_t start_nonce, uint32_t count,
                        uint32_t target_top, uint32_t* out_nonces, uint32_t max_out,
                        uint32_t* out_found);

// Roda o vetor de teste SHA-256d (bloco gênese do Bitcoin) na GPU e confere o resultado. 0 = passou.
GLM_EXPORT int glm_self_test(void);

#ifdef __cplusplus
}
#endif
