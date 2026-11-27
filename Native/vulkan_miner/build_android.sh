#!/usr/bin/env bash
# Compila libvulkanminer.so para Android (arm64-v8a) e copia para o projeto.
# Requisitos: ANDROID_NDK_HOME definido e cmake + ninja no PATH.
set -euo pipefail
: "${ANDROID_NDK_HOME:?Defina ANDROID_NDK_HOME com a pasta do Android NDK}"
ABI=arm64-v8a
SRC="$(cd "$(dirname "$0")" && pwd)"
BUILD="$SRC/build-$ABI"
DEST="$SRC/../../Platforms/Android/lib/$ABI"

cmake -S "$SRC" -B "$BUILD" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=$ABI -DANDROID_PLATFORM=android-26 -DCMAKE_BUILD_TYPE=Release
cmake --build "$BUILD"
mkdir -p "$DEST"
cp "$BUILD/libvulkanminer.so" "$DEST/"
echo "Pronto: $DEST/libvulkanminer.so"
