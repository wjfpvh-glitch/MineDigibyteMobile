#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define VKM_EXPORT __attribute__((visibility("default")))

// 0 = ok; negativo = erro (veja vkm_last_error()).
VKM_EXPORT int vkm_init(void);
VKM_EXPORT void vkm_shutdown(void);
VKM_EXPORT const char* vkm_device_name(void);
VKM_EXPORT const char* vkm_last_error(void);

// Varre `count` nonces a partir de start_nonce no cabeçalho de 80 bytes.
// Devolve (até max_out) nonces cujo hash tem os 32 bits mais significativos
// <= target_top. O chamador deve confirmar cada candidato com o alvo completo.
VKM_EXPORT int vkm_scan(const uint8_t* header80, uint32_t start_nonce, uint32_t count,
                        uint32_t target_top, uint32_t* out_nonces, uint32_t max_out,
                        uint32_t* out_found);

// Roda o vetor de teste SHA-256d (bloco gênese do Bitcoin) na GPU e confere o resultado. 0 = passou.
VKM_EXPORT int vkm_self_test(void);

#ifdef __cplusplus
}
#endif
