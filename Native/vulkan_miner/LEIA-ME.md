# Backend Vulkan (GPU) do MineDigibyteMobile

Compute shader SHA-256d em Vulkan, chamado pelo C# via P/Invoke.

## Como compilar (Windows)

1. Instale o **Android NDK** (Visual Studio > Android SDK Manager, ou Android Studio).
2. Defina `ANDROID_NDK_HOME` com a pasta do NDK, por exemplo
   `C:\Program Files (x86)\Android\android-sdk\ndk\27.0.12077973`.
3. Tenha o `cmake` no PATH (vem com o Android SDK, em `cmake\<versão>\bin`).
4. Rode `build_android.bat`.

Ele gera `Platforms\Android\lib\arm64-v8a\libvulkanminer.so`. O `.csproj` inclui o
arquivo no APK automaticamente quando ele existe. Depois, compile e instale o app.

No Linux ou macOS use `build_android.sh`.

## O que acontece no celular

1. Ao abrir o app, `MiningSessionRuntime.InitializeGpu()` inicia o Vulkan.
2. A GPU roda um **auto-teste** com um vetor de teste SHA-256d conhecido (o bloco gênese do Bitcoin, usado só como teste; o DigiByte usa o mesmo hash SHA-256d). Só se o resultado
   for exato a GPU é usada; senão o app segue na CPU e o motivo aparece no log.
3. Durante a mineração, cada lote de ~1 milhão de nonces vai para a GPU. Ela devolve
   candidatos (hash com os 32 bits de topo <= alvo) e o C# confirma cada um no CPU
   antes de enviar a share.

## Testes que rodam sem Android

```
python3 tests/check_against_hashlib.py
```

Compara o caminho midstate + nonce (o mesmo do shader) com o `hashlib` do Python.
