cmake_minimum_required(VERSION 3.18)
project(vulkanminer CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)

# glslc vem dentro do Android NDK (shader-tools/<host>/glslc).
file(GLOB _ndk_shader_dirs "${ANDROID_NDK}/shader-tools/*")
find_program(GLSLC glslc HINTS ${_ndk_shader_dirs} NO_DEFAULT_PATH)
if(NOT GLSLC)
  find_program(GLSLC glslc)
endif()
if(NOT GLSLC)
  message(FATAL_ERROR "glslc não encontrado. Use um Android NDK recente (shader-tools) ou instale o Vulkan SDK.")
endif()

set(SPV_BIN "${CMAKE_CURRENT_BINARY_DIR}/sha256d.spv")
set(SPV_INC "${CMAKE_CURRENT_BINARY_DIR}/sha256d.spv.inc")

add_custom_command(
  OUTPUT "${SPV_INC}"
  COMMAND "${GLSLC}" --target-env=vulkan1.0 -O -o "${SPV_BIN}" "${CMAKE_CURRENT_SOURCE_DIR}/shaders/sha256d.comp"
  COMMAND "${CMAKE_COMMAND}" -DSPV=${SPV_BIN} -DOUT=${SPV_INC} -P "${CMAKE_CURRENT_SOURCE_DIR}/embed_spv.cmake"
  DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/shaders/sha256d.comp" "${CMAKE_CURRENT_SOURCE_DIR}/embed_spv.cmake"
  COMMENT "Compilando sha256d.comp para SPIR-V")

add_library(vulkanminer SHARED vulkan_miner.cpp "${SPV_INC}")
target_include_directories(vulkanminer PRIVATE "${CMAKE_CURRENT_BINARY_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(vulkanminer PRIVATE vulkan log)
