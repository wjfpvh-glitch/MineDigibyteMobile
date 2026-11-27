# Converte um .spv binário em uma lista de uint32 (little-endian) para #include.
file(READ "${SPV}" hex HEX)
string(LENGTH "${hex}" len)
math(EXPR rem "${len} % 8")
if(NOT rem EQUAL 0)
  message(FATAL_ERROR "SPIR-V inválido: tamanho não é múltiplo de 4 bytes")
endif()
string(REGEX REPLACE "([0-9a-f][0-9a-f])([0-9a-f][0-9a-f])([0-9a-f][0-9a-f])([0-9a-f][0-9a-f])"
       "0x\\4\\3\\2\\1," words "${hex}")
file(WRITE "${OUT}" "${words}\n")
