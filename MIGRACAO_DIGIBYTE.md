# MineDigibyteMobile — migração de MineBitcoinMobile

- DigiByte tem o algoritmo **SHA-256d** (mesmo hash do Bitcoin), então a lógica de mineração (Stratum, CPU, Vulkan, OpenGL ES) foi mantida sem alterações.
- Renomeado: projeto, namespace, nome do app, ID do pacote (`com.companyname.minedigibytemobile`), serviços Android, notificações, user-agent Stratum e worker padrão.
- Interface: textos em PT-BR, EN e ES, cor de destaque (azul), logo do app e tela de carregamento novos.
- Doação: endereço DGB configurado em `MainPage.xaml.cs` (`DonationDigiByteAddress`). QR usa o esquema `digibyte:`.
- Pool/porta padrão: `stratum.bcmonster.com` porta `9996` (stratum+tcp). Pode ser trocada nas configurações por qualquer pool DigiByte **SHA-256d**. Usuário normalmente é `ENDERECO_DGB.worker`, senha `x`.
- O auto-teste da GPU continua usando o bloco gênese do Bitcoin só como vetor de teste SHA-256d.
- Não foi possível compilar aqui (sem SDK .NET/MAUI). Compile no seu ambiente antes de instalar.
