# MineDigibyteMobile

**Open-source DigiByte (DGB) mining client for Android — built with .NET MAUI.**

[Português](#português) · [English](#english)

---

## Português

### Sobre o projeto

O MineDigibyteMobile nasceu de uma preocupação: encontrar aplicativos de mineração móvel confiáveis e transparentes pode ser difícil. Muitos aplicativos apresentados como mineração podem apenas simular resultados ou usar modelos fechados; outros podem limitar o usuário a pools específicas, cobrar taxas que tornam saques pouco viáveis ou não explicar claramente como os resultados são calculados.

Não afirmamos que todos os aplicativos de mineração disponíveis na Play Store tenham esses problemas. Essa foi a motivação do autor para criar uma alternativa aberta, na qual o usuário possa configurar a conexão de mineração e acompanhar o trabalho realizado.

O objetivo do MineDigibyteMobile é oferecer transparência e controle ao usuário. O aplicativo se conecta a pools DigiByte compatíveis com **SHA-256d** usando o protocolo Stratum. O pagamento de recompensas, quando houver, é administrado pela pool escolhida — o aplicativo não controla nem garante pagamentos.

### Características e estado atual

- Aplicativo Android desenvolvido com .NET MAUI / C#.
- Mineração SHA-256d por CPU através de conexão Stratum.
- Configuração de pool, porta, usuário/worker e senha.
- Interface com suporte a português, inglês e espanhol.
- Monitoramento da temperatura da bateria reportada pelo Android.
- Redução temporária de intensidade e pausa por proteção térmica, conforme os limites documentados no código.
- Endereço de doação DGB voluntária do projeto original.

**Limitação importante:** a mineração GPU real ainda não está implementada/validada nesta versão. Não considere detecção preliminar de hardware ou bibliotecas nativas ausentes como suporte funcional a GPU. Consulte [STATUS_DA_ETAPA.md](STATUS_DA_ETAPA.md).

### Avisos sobre mineração

- Mineração em celular não garante lucro e pode gerar recompensas muito pequenas ou nenhuma recompensa.
- Hashrate, dificuldade da rede, regras da pool, taxa da pool, conectividade e saldo mínimo de saque afetam os resultados.
- Pools são serviços de terceiros. Verifique taxas, regras de pagamento, reputação e requisitos antes de conectar.
- Mineração contínua consome bateria e pode aquecer o aparelho. A proteção térmica reduz riscos, mas não garante que o dispositivo não superaqueça. Interrompa o uso se notar aquecimento excessivo.
- Use somente carteiras e endereços que você controla. Confira o endereço antes de iniciar.
- O projeto não é afiliado, patrocinado ou endossado oficialmente pela DigiByte Foundation, pela rede DigiByte ou por operadores de pools.

### Doação voluntária

Se desejar apoiar o desenvolvimento, você pode enviar DigiByte (DGB) para:

`dgb1q269vr89kzrfg3vfa9upae32fqkfh2uvf7d0jg2`

Doações são voluntárias e não são necessárias para instalar ou usar o aplicativo. Confira a rede e o endereço antes de enviar. O desenvolvedor nunca solicitará sua chave privada ou frase de recuperação.

### Baixar

- **Código-fonte:** use o botão **Code → Download ZIP** nesta página ou clone o repositório.
- **APK:** baixe somente os APKs anexados às [Releases oficiais](https://github.com/wjfpvh-glitch/MineDigibyteMobile/releases), quando publicados.
- Verifique sempre se a release foi publicada neste repositório original. Não instale APKs de forks ou páginas que se apresentem como oficiais sem confirmação.

### Compilar

Requisitos gerais:
- Windows com .NET 10 SDK e workload .NET MAUI Android.
- Android SDK e ferramentas de build compatíveis.
- JDK compatível com a versão do Android workload instalada.

Comandos básicos no PowerShell:

```powershell
dotnet workload install maui-android
dotnet restore .\MineDigibyteMobile.csproj
dotnet build .\MineDigibyteMobile.csproj -f net10.0-android
```

A configuração exata do ambiente pode variar conforme as versões do .NET, Android SDK e workloads. Uma compilação bem-sucedida não substitui testes em dispositivo real.

### Identidade do projeto e versões derivadas

O repositório em `github.com/wjfpvh-glitch/MineDigibyteMobile` é a fonte original deste projeto e é mantido pelo seu criador e fundador, **wjfpvh-glitch**.

Somente releases publicadas neste repositório são releases oficiais do projeto original. Cópias, forks e versões modificadas por terceiros não são publicadas, mantidas, verificadas ou endossadas pelo autor original, a menos que exista declaração explícita neste repositório.

Quem redistribuir uma versão modificada deve identificar claramente que se trata de um fork/versão modificada, usar identidade própria e não sugerir que o autor original a publicou ou aprovou. A licença MIT exige preservar os avisos de copyright e licença aplicáveis; ela não concede direito de usar marcas, identidade visual ou de alegar endosso do projeto original.

### Governança e segurança

- O repositório é público para leitura e download.
- Somente o proprietário do repositório administra o projeto e publica releases oficiais.
- Pull requests e contribuições externas não são aceitos para incorporação na linha oficial, salvo anúncio explícito em contrário pelo proprietário.
- Nunca publique keystores, senhas, tokens, chaves privadas, frases de recuperação ou arquivos de assinatura.
- Problemas de segurança devem ser comunicados ao proprietário de forma responsável; não publique segredos ou dados pessoais em issues.

### Licença

Este projeto é distribuído sob a licença **MIT**. Consulte o arquivo [LICENSE](LICENSE). Bibliotecas e dependências de terceiros permanecem sob suas próprias licenças.

---

## English

### About

MineDigibyteMobile was created out of concern that trustworthy and transparent mobile mining applications can be difficult to identify. Some apps marketed as miners may simulate results or use closed systems; others may restrict users to selected pools, charge fees that make withdrawals impractical, or fail to explain how results are calculated.

This is not a claim that every mining app on the Play Store has these problems. It describes the motivation behind creating an open alternative where users can configure their mining connection and monitor the work performed.

MineDigibyteMobile aims to give users transparency and control. The app connects to DigiByte pools compatible with **SHA-256d** through the Stratum protocol. Any mining payout is handled by the selected pool; the app does not control or guarantee payments.

### Features and current status

- Android application built with .NET MAUI / C#.
- SHA-256d CPU mining through a Stratum connection.
- Configurable pool, port, username/worker and password.
- Portuguese, English and Spanish interface localization.
- Monitoring of the battery temperature reported by Android.
- Temporary intensity reduction and thermal pause according to the limits documented in the code.
- Optional DGB donation address for the original project.

**Important limitation:** real GPU mining is not yet implemented/validated in this version. Preliminary hardware detection or absent native libraries must not be interpreted as working GPU support. See [STATUS_DA_ETAPA.md](STATUS_DA_ETAPA.md).

### Mining and safety notices

- Mobile mining is not guaranteed to be profitable and may produce very small rewards or no rewards.
- Hashrate, network difficulty, pool rules and fees, connectivity, and minimum payout thresholds affect results.
- Pools are third-party services. Review their fees, payout rules, reputation, and requirements before connecting.
- Continuous mining consumes battery power and can heat the device. Thermal safeguards reduce risk but cannot guarantee that overheating will not occur. Stop mining if the device becomes excessively hot.
- Use only wallets and addresses you control. Verify the address before starting.
- This project is not officially affiliated with, sponsored by, or endorsed by the DigiByte Foundation, the DigiByte network, or any pool operator.

### Voluntary donation

To support development, you may voluntarily donate DigiByte (DGB) to:

`dgb1q269vr89kzrfg3vfa9upae32fqkfh2uvf7d0jg2`

Donations are optional and are not required to install or use the app. Verify the network and address before sending. The developer will never ask for your private key or recovery phrase.

### Downloads

- **Source code:** use **Code → Download ZIP** on this page or clone the repository.
- **APK:** download APKs only from the [official Releases page](https://github.com/wjfpvh-glitch/MineDigibyteMobile/releases), when available.
- Confirm that a release was published in this original repository. Do not assume APKs from forks or other pages are official.

### Build

General requirements:
- Windows with the .NET 10 SDK and .NET MAUI Android workload.
- Android SDK and compatible build tools.
- A JDK compatible with the installed Android workload.

Basic PowerShell commands:

```powershell
dotnet workload install maui-android
dotnet restore .\MineDigibyteMobile.csproj
dotnet build .\MineDigibyteMobile.csproj -f net10.0-android
```

Exact environment requirements may vary with .NET, Android SDK, and workload versions. A successful build does not replace testing on a physical device.

### Project identity and modified versions

The repository at `github.com/wjfpvh-glitch/MineDigibyteMobile` is the original source of this project and is maintained by its creator and founder, **wjfpvh-glitch**.

Only releases published in this repository are official releases of the original project. Copies, forks, and third-party modified versions are not published, maintained, verified, or endorsed by the original author unless this repository explicitly states otherwise.

Anyone redistributing a modified version should clearly identify it as a fork/modified version, use its own identity, and not imply that the original author published or approved it. The MIT license requires preservation of applicable copyright and license notices; it does not grant rights to use the original project's trademarks, branding, or imply endorsement.

### Governance and security

- The repository is public for viewing and downloading.
- Only the repository owner administers the project and publishes official releases.
- External pull requests and contributions are not accepted for inclusion in the official line unless the owner explicitly announces otherwise.
- Never publish keystores, passwords, tokens, private keys, recovery phrases, or signing files.
- Report security issues responsibly to the owner; do not post secrets or personal data in issues.

### License

This project is distributed under the **MIT License**. See [LICENSE](LICENSE). Third-party libraries and dependencies remain under their respective licenses.
