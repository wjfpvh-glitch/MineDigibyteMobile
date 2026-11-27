// Lê linhas "header_hex nonce_decimal" da entrada padrão e imprime
// "digest_hex top_word_hex" calculados pelo mesmo caminho do shader.
#include <cstdio>
#include <cstdlib>
#include <string>
#include <iostream>
#include "../sha256_cpu.h"

static int hexval(char c) { return c <= '9' ? c - '0' : (c | 32) - 'a' + 10; }

int main() {
    std::string hex, nonce_s;
    while (std::cin >> hex >> nonce_s) {
        uint8_t header[80];
        for (int i = 0; i < 80; i++) header[i] = uint8_t(hexval(hex[2 * i]) * 16 + hexval(hex[2 * i + 1]));
        uint32_t nonce = uint32_t(std::strtoul(nonce_s.c_str(), nullptr, 10));
        vkm::JobConstants job = vkm::prepare_job(header);  // nonce dos bytes 76..79 é ignorado
        uint32_t words[8];
        vkm::double_sha256_words(job, nonce, words);
        uint8_t digest[32];
        vkm::words_to_digest(words, digest);
        for (int i = 0; i < 32; i++) std::printf("%02x", digest[i]);
        std::printf(" %08x\n", vkm::hash_top_word(words));
    }
    return 0;
}
