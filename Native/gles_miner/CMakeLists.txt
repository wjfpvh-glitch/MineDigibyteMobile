cmake_minimum_required(VERSION 3.18)
project(glesminer CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)

add_library(glesminer SHARED gles_miner.cpp)
target_include_directories(glesminer PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(glesminer PRIVATE EGL GLESv3 log)
