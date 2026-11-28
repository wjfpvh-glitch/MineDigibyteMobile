@echo off
rem Compila as bibliotecas de GPU (Vulkan e OpenGL ES) para 64 bits e 32 bits.
rem Coloque este arquivo na pasta Native e de dois cliques.
setlocal
if "%ANDROID_SDK_ROOT%"=="" set ANDROID_SDK_ROOT=%ANDROID_HOME%
if "%ANDROID_SDK_ROOT%"=="" set ANDROID_SDK_ROOT=%LOCALAPPDATA%\Android\Sdk
if "%ANDROID_NDK_HOME%"=="" set ANDROID_NDK_HOME=%ANDROID_SDK_ROOT%\ndk\27.0.12077973
if "%CMAKE_BIN%"=="" set CMAKE_BIN=%ANDROID_SDK_ROOT%\cmake\3.22.1\bin
set ROOT=%~dp0
if "%ROOT:~-1%"=="\" set ROOT=%ROOT:~0,-1%

if not exist "%CMAKE_BIN%\cmake.exe" ( echo cmake.exe nao encontrado em %CMAKE_BIN% & pause & exit /b 1 )
if not exist "%CMAKE_BIN%\ninja.exe" ( echo ninja.exe nao encontrado em %CMAKE_BIN% & pause & exit /b 1 )
if not exist "%ANDROID_NDK_HOME%\build\cmake\android.toolchain.cmake" ( echo NDK nao encontrado em %ANDROID_NDK_HOME% & pause & exit /b 1 )

call :build vulkan_miner libvulkanminer.so arm64-v8a 26 || goto :fail
call :build vulkan_miner libvulkanminer.so armeabi-v7a 26 || goto :fail
call :build gles_miner libglesminer.so arm64-v8a 21 || goto :fail
call :build gles_miner libglesminer.so armeabi-v7a 21 || goto :fail

echo.
echo Tudo pronto. As bibliotecas estao em Platforms\Android\lib\
pause
exit /b 0

:fail
echo.
echo ERRO: veja a mensagem acima.
pause
exit /b 1

:build
set SRC=%ROOT%\%~1
set LIB=%~2
set ABI=%~3
set API=%~4
set BUILD=%SRC%\build-%ABI%
set DEST=%ROOT%\..\Platforms\Android\lib\%ABI%
echo.
echo === %~1 / %ABI% / Android API %API% ===
"%CMAKE_BIN%\cmake.exe" -S "%SRC%" -B "%BUILD%" -G Ninja -DCMAKE_MAKE_PROGRAM="%CMAKE_BIN%\ninja.exe" -DCMAKE_TOOLCHAIN_FILE="%ANDROID_NDK_HOME%\build\cmake\android.toolchain.cmake" -DANDROID_ABI=%ABI% -DANDROID_PLATFORM=android-%API% -DCMAKE_BUILD_TYPE=Release
if errorlevel 1 exit /b 1
"%CMAKE_BIN%\cmake.exe" --build "%BUILD%"
if errorlevel 1 exit /b 1
if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%BUILD%\%LIB%" "%DEST%\" >nul
if errorlevel 1 exit /b 1
echo OK: %LIB% (%ABI%)
exit /b 0
