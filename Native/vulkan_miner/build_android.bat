@echo off
rem Compila libvulkanminer.so para Android (arm64-v8a) e copia para o projeto.
rem Requisitos: Android NDK instalado e a variavel ANDROID_NDK_HOME apontando para ele,
rem e o CMake (vem com o Android SDK ou com o Visual Studio) no PATH.
setlocal
if "%ANDROID_NDK_HOME%"=="" (
  echo Defina ANDROID_NDK_HOME com a pasta do Android NDK.
  exit /b 1
)
set ABI=arm64-v8a
set SRC=%~dp0
set SRC=%SRC:~0,-1%
set BUILD=%SRC%\build-%ABI%
set DEST=%SRC%\..\..\Platforms\Android\lib\%ABI%
set NINJA=%ANDROID_NDK_HOME%\prebuilt\windows-x86_64\bin\ninja.exe
if not exist "%NINJA%" (
  for %%I in (ninja.exe) do set NINJA=%%~$PATH:I
)
if not exist "%NINJA%" (
  echo Ninja nao encontrado. Adicione o CMake do Android SDK ao PATH.
  exit /b 1
)

cmake -S "%SRC%" -B "%BUILD%" -G Ninja -DCMAKE_MAKE_PROGRAM="%NINJA%" -DCMAKE_TOOLCHAIN_FILE="%ANDROID_NDK_HOME%\build\cmake\android.toolchain.cmake" -DANDROID_ABI=%ABI% -DANDROID_PLATFORM=android-26 -DCMAKE_BUILD_TYPE=Release || exit /b 1
cmake --build "%BUILD%" || exit /b 1

if not exist "%DEST%" mkdir "%DEST%"
copy /Y "%BUILD%\libvulkanminer.so" "%DEST%\" || exit /b 1
echo Pronto: %DEST%\libvulkanminer.so
