# Backend OpenGL ES 3.1 (GPU) do MineDigibyteMobile

Plano B para aparelhos sem Vulkan. Usa um compute shader (OpenGL ES 3.1), com o
mesmo algoritmo do backend Vulkan. O app tenta Vulkan primeiro e, se não der,
OpenGL ES. Se nenhum passar no auto-teste (vetor de teste SHA-256d: bloco gênese do Bitcoin, usado só como teste), usa a CPU.

Aparelhos só com OpenGL ES 3.0 (sem 3.1) não têm compute shader e ficam na CPU.

Para compilar tudo (Vulkan + OpenGL ES, 64 e 32 bits), rode `Native\build_all.bat`.
