@echo off
rem Compila libvulkanminer.so (Vulkan) para Android arm64-v8a com os caminhos do wjfpv.
rem Coloque este arquivo em Native\vulkan_miner e de dois cliques (ou rode no cmd).
setlocal
set SDK=C:\Users\wjfpv\AppData\Local\Android\Sdk
set ANDROID_NDK_HOME=%SDK%\ndk\27.0.12077973
set CMAKE_BIN=%SDK%\cmake\3.22.1\bin
set ABI=arm64-v8a
set SRC=%~dp0
rem Tira a barra final: "pasta\" dentro de aspas escapa a aspa e quebra o comando.
if "%SRC:~-1%"=="\" set SRC=%SRC:~0,-1%
set BUILD=%SRC%\build-%ABI%
set DEST=%SRC%\..\..\Platforms\Android\lib\%ABI%

if not exist "%CMAKE_BIN%\cmake.exe" (
  echo cmake.exe nao encontrado em %CMAKE_BIN%
  pause
  exit /b 1
)
if not exist "%CMAKE_BIN%\ninja.exe" (
  echo ninja.exe nao encontrado em %CMAKE_BIN%
  pause
  exit /b 1
)
if not exist "%ANDROID_NDK_HOME%\build\cmake\android.toolchain.cmake" (
  echo NDK nao encontrado em %ANDROID_NDK_HOME%
  pause
  exit /b 1
)

"%CMAKE_BIN%\cmake.exe" -S "%SRC%" -B "%BUILD%" -G Ninja ^
  -DCMAKE_MAKE_PROGRAM="%CMAKE_BIN%\ninja.exe" ^
  -DCMAKE_TOOLCHAIN_FILE="%ANDROID_NDK_HOME%\build\cmake\android.toolchain.cmake" ^
  -DANDROID_ABI=%ABI% -DANDROID_PLATFORM=android-26 -DCMAKE_BUILD_TYPE=Release
if errorlevel 1 ( echo ERRO na configuracao do CMake & pause & exit /b 1 )

"%CMAKE_BIN%\cmake.exe" --build "%BUILD%"
if errorlevel 1 ( echo ERRO na compilacao & pause & exit /b 1 )

if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%BUILD%\libvulkanminer.so" "%DEST%\"
if errorlevel 1 ( echo ERRO ao copiar a biblioteca & pause & exit /b 1 )

echo.
echo Pronto: %DEST%\libvulkanminer.so
pause
