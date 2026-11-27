// SHA-256 no CPU usado pelo código nativo para preparar o trabalho (midstate)
// e como referência dos testes. A função double_sha256_digest espelha, passo
// a passo, o que o shader sha256d.comp faz na GPU.
#pragma once
#include <cstdint>
#include <cstring>

namespace vkm {

static const uint32_t K[64] = {
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};

static const uint32_t IV[8] = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
                               0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};

inline uint32_t rotr(uint32_t x, int n) { return (x >> n) | (x << (32 - n)); }
inline uint32_t bswap32(uint32_t x) {
    return (x >> 24) | ((x >> 8) & 0x0000ff00u) | ((x << 8) & 0x00ff0000u) | (x << 24);
}
inline uint32_t be32(const uint8_t* p) {
    return (uint32_t(p[0]) << 24) | (uint32_t(p[1]) << 16) | (uint32_t(p[2]) << 8) | p[3];
}

// Compressão de um bloco de 64 bytes. Usa janela circular de 16 palavras para
// o cronograma de mensagem (a mesma técnica do shader, que poupa registradores).
// W[i] = s1(W[i-2]) + W[i-7] + s0(W[i-15]) + W[i-16].
inline void compress(uint32_t state[8], const uint32_t block[16]) {
    uint32_t w[16];
    for (int i = 0; i < 16; i++) w[i] = block[i];
    uint32_t a = state[0], b = state[1], c = state[2], d = state[3];
    uint32_t e = state[4], f = state[5], g = state[6], h = state[7];
    for (int i = 0; i < 64; i++) {
        uint32_t wi;
        if (i < 16) {
            wi = w[i];
        } else {
            uint32_t w15 = w[(i + 1) & 15], w2 = w[(i + 14) & 15];
            uint32_t s0 = rotr(w15, 7) ^ rotr(w15, 18) ^ (w15 >> 3);
            uint32_t s1 = rotr(w2, 17) ^ rotr(w2, 19) ^ (w2 >> 10);
            wi = w[i & 15] + s0 + w[(i + 9) & 15] + s1;
            w[i & 15] = wi;
        }
        uint32_t S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
        uint32_t ch = (e & f) ^ (~e & g);
        uint32_t t1 = h + S1 + ch + K[i] + wi;
        uint32_t S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
        uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
        uint32_t t2 = S0 + maj;
        h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
    }
    state[0] += a; state[1] += b; state[2] += c; state[3] += d;
    state[4] += e; state[5] += f; state[6] += g; state[7] += h;
}

// Constantes por trabalho: o primeiro bloco (bytes 0..63 do cabeçalho) não
// depende do nonce, então seu estado (midstate) é calculado uma vez no CPU.
struct JobConstants {
    uint32_t midstate[8];
    uint32_t tail[3];  // palavras big-endian dos bytes 64..75 (merkle, ntime, nbits)
};

inline JobConstants prepare_job(const uint8_t header[80]) {
    JobConstants j;
    std::memcpy(j.midstate, IV, sizeof(IV));
    uint32_t w[16];
    for (int i = 0; i < 16; i++) w[i] = be32(header + 4 * i);
    compress(j.midstate, w);
    j.tail[0] = be32(header + 64);
    j.tail[1] = be32(header + 68);
    j.tail[2] = be32(header + 72);
    return j;
}

// SHA-256(SHA-256(cabeçalho com este nonce)). `nonce` é o valor numérico que
// o cabeçalho (Bitcoin/DigiByte SHA-256d) grava em little-endian nos bytes 76..79; na palavra big-endian do
// SHA-256 isso vira bswap32(nonce). out_words recebe as 8 palavras finais.
inline void double_sha256_words(const JobConstants& j, uint32_t nonce, uint32_t out_words[8]) {
    uint32_t st[8];
    std::memcpy(st, j.midstate, sizeof(st));
    uint32_t w[16] = {j.tail[0], j.tail[1], j.tail[2], bswap32(nonce),
                      0x80000000u, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 640u};
    compress(st, w);

    uint32_t st2[8];
    std::memcpy(st2, IV, sizeof(st2));
    uint32_t w2[16] = {st[0], st[1], st[2], st[3], st[4], st[5], st[6], st[7],
                       0x80000000u, 0, 0, 0, 0, 0, 0, 256u};
    compress(st2, w2);
    std::memcpy(out_words, st2, sizeof(st2));
}

// Palavra mais significativa do hash visto como número little-endian de 256 bits.
inline uint32_t hash_top_word(const uint32_t words[8]) { return bswap32(words[7]); }

inline void words_to_digest(const uint32_t words[8], uint8_t out[32]) {
    for (int i = 0; i < 8; i++) {
        out[4 * i] = uint8_t(words[i] >> 24);
        out[4 * i + 1] = uint8_t(words[i] >> 16);
        out[4 * i + 2] = uint8_t(words[i] >> 8);
        out[4 * i + 3] = uint8_t(words[i]);
    }
}

}  // namespace vkm
