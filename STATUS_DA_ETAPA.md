# MineDigibyteMobile — estado desta entrega

## Alterações incluídas nesta revisão

- Monitoramento periódico (a cada 1 segundo) do nível/estado da bateria e da temperatura reportada pelo Android.
- Proteção térmica com histerese baseada na temperatura da bateria:
  - a partir de 42 °C: limita temporariamente a intensidade para até 25%;
  - a partir de 45 °C: solicita pausa da mineração;
  - abaixo de 40 °C: permite retomar a mineração que foi pausada pela proteção e restaurar a intensidade configurada.
- A intensidade configurada pelo usuário não é sobrescrita nas preferências; a limitação é temporária.
- A interface informa o estado da proteção térmica.

## Limitações importantes

- A leitura de temperatura é a temperatura da bateria fornecida pelo Android (`ACTION_BATTERY_CHANGED`). Ela não é uma leitura direta da CPU, GPU, SoC ou superfície do aparelho. Alguns fabricantes podem não fornecer esse dado corretamente.
- A proteção térmica reduz o risco, mas não garante que o aparelho não superaqueça. O usuário deve interromper a mineração se notar aquecimento excessivo.
- Esta entrega **não implementa ainda mineração GPU real**. O código recebido contém detecção preliminar de capacidades, mas não contém um backend Vulkan Compute executável, shader SPIR-V validado, nem backends OpenGL ES Compute/OpenCL. Nenhum desses motores foi ativado ou anunciado como funcional.
- Não foi possível compilar/testar o projeto neste ambiente porque o SDK .NET/MAUI não está instalado aqui. O projeto deve ser aberto no ambiente Windows/Android do desenvolvedor e compilado antes de instalar.

## Próxima etapa necessária para GPU real

Implementar e validar separadamente a camada nativa Vulkan (incluindo criação de instância/dispositivo/fila/pipeline, buffers, despacho do shader SHA-256d e leitura dos resultados), comparar vetores de teste com a CPU e só então conectar o backend ao ciclo Stratum. OpenGL ES Compute e OpenCL exigem backends nativos próprios e detecção real de extensões/dispositivos.
