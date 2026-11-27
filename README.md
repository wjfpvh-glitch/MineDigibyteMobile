# MineDigibyteMobile

**Open-source DigiByte (DGB) mining client for Android, built with .NET MAUI.**

## About the project

MineDigibyteMobile was created because finding trustworthy and transparent mobile-mining applications can be difficult. Some apps presented as miners may simulate mining results or operate as closed systems. Others may restrict users to selected pools, charge fees that make withdrawals impractical, or fail to explain clearly how mining results are calculated.

This is the motivation behind this project, not a claim that every mining app on the Play Store has these problems. MineDigibyteMobile was created as an open-source alternative intended to give users more visibility into their mining configuration and the work performed by the app.

The application connects to compatible DigiByte **SHA-256d** mining pools using the Stratum protocol. Pool payouts, fees, minimum withdrawal amounts, and payment schedules are controlled by the pool selected by the user. The app does not control or guarantee mining payouts.

## Features and current status

- Android application built with .NET MAUI and C#.
- SHA-256d CPU mining through a Stratum connection.
- Configurable pool address, port, username/worker, and password.
- Portuguese, English, and Spanish interface localization.
- Battery-temperature monitoring using information reported by Android.
- Thermal intensity reduction and mining pause according to the safeguards implemented in the source code.
- Optional DGB donation address supporting the original project.

**GPU limitation:** GPU mining must not be considered available unless and until it is implemented and validated in a published version. Hardware detection or references to native libraries alone do not mean GPU mining is functional. Check the project source and release notes for the actual status.

## Mining, profitability, and safety

- Mobile mining is not guaranteed to be profitable. It may generate very small rewards or no rewards at all.
- Results depend on device performance, hashrate, network difficulty, pool rules and fees, connectivity, and minimum payout thresholds.
- Mining pools are third-party services. Review their fees, payout policies, reputation, and requirements before connecting.
- Continuous mining consumes battery power and can heat the device. Thermal safeguards can reduce risk but cannot guarantee that overheating will not occur. Stop mining if the device becomes excessively hot.
- Use only wallet addresses you control, and verify the address and network before mining.
- This project is independent and is not officially affiliated with, sponsored by, or endorsed by the DigiByte Foundation, the DigiByte network, or any mining pool operator.

## Voluntary DGB donations

Users who wish to support development may voluntarily donate DigiByte (DGB) to the original project:

`dgb1q269vr89kzrfg3vfa9upae32fqkfh2uvf7d0jg2`

Donations are optional and are not required to install or use the application. Always verify the network and address before sending funds. The developer will never ask for your private keys or wallet recovery phrase.

## Downloads

- **Source code:** use **Code → Download ZIP** on this repository page, or clone the repository.
- **Android APK:** download APK files only from the [official Releases page](https://github.com/wjfpvh-glitch/MineDigibyteMobile/releases), when published.
- A release is an official release of the original project only when it is published in this repository. APKs from forks or unrelated websites are not official releases of this project.

## Building from source

General requirements:

- Windows with the .NET 10 SDK and the .NET MAUI Android workload.
- Android SDK and compatible Android build tools.
- A JDK compatible with the installed .NET Android workload.

Basic PowerShell commands:

```powershell
dotnet workload install maui-android
dotnet restore .\MineDigibyteMobile.csproj
dotnet build .\MineDigibyteMobile.csproj -f net10.0-android
```

Exact environment requirements may vary with the installed .NET SDK, Android SDK, JDK, and workloads. A successful build does not replace testing on a physical Android device.

## Original project, authorship, and forks

The repository at [github.com/wjfpvh-glitch/MineDigibyteMobile](https://github.com/wjfpvh-glitch/MineDigibyteMobile) is the original source repository for this project.

**Creator and founder: wjfpvh-glitch.**

Only releases published in this original repository are official releases of MineDigibyteMobile. Copies, forks, and third-party modified versions are not published, maintained, verified, or endorsed by the original creator unless this repository explicitly says otherwise.

Anyone redistributing a modified version should clearly identify it as a fork or modified version, use a distinct project name and branding, and never imply that the original creator published or approved it. The MIT License requires preservation of applicable copyright and license notices. It does not grant permission to use the original project's name, branding, or identity in a way that implies endorsement.

## Project governance and security

- This repository is public so anyone can inspect and download the source code.
- The repository owner is responsible for the original project, its official releases, and changes to the main repository.
- External contributions and pull requests are not accepted for inclusion in the official project unless the owner explicitly announces otherwise.
- Never commit Android signing keystores, passwords, access tokens, private keys, wallet recovery phrases, or other secrets.
- Report security issues responsibly to the repository owner. Do not publish secrets or personal information in public issues.

## License

MineDigibyteMobile is distributed under the **MIT License**. See [LICENSE](LICENSE) for the complete license text. Third-party libraries and dependencies remain subject to their respective licenses.
