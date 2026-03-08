# Openpilot Toolkit (OPTK)

Openpilot Toolkit is a class library and set of host apps for interacting with openpilot and comma devices.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
- [Usage](#usage)
- [Screenshots](#screenshots)
- [Development](#development)
- [Contributing](#contributing)
- [License](#license)
- [Support](#support)


## Overview

Openpilot Toolkit streamlines common tasks for openpilot device owners. The toolkit bundles desktop and Android utilities for managing SSH keys, transferring files, reviewing drive data, and installing forks while providing an approachable UI for everyday workflows.

## Features

### Windows App
- **Drive Video Player:** Playback and raw export of all camera footage.
- **SSH Wizard:** Easily generate and install SSH keys.
- **Remote Control:** Access common functions remotely.
- **Fork Installer:** Simple installation of different openpilot forks.
- **Fingerprint V2 Viewer:** View your vehicle's fingerprint.
- **SSH File Explorer:** Browse, edit, delete, and upload files.
- **SSH Terminal:** Full terminal emulation for direct device access.

### Android App
- **SSH Wizard:** Easily generate and install SSH keys.
- **Fork Installer:** Simple installation of different openpilot forks.

### Cross-Platform CLI
- **Linux/macOS-Friendly Host:** Run discovery and core device operations from any machine with .NET 8.
- **Host SSH Key Reuse:** Uses an existing private key from `--ssh-key`, `OPENPILOT_SSH_KEY`, or standard `~/.ssh/*` paths.
- **Device Management:** Discover devices, list routes, reboot, shut down, and install forks without the Windows GUI.

## Getting Started

### Prerequisites
- Windows 10/11 (x64) for the desktop application
- Android device (APK sideload) for the mobile application
- .NET 8 SDK for the cross-platform CLI
- An existing SSH private key that already works with your comma/openpilot device
- `ffmpeg` on `PATH` if you plan to use media export features from a non-Windows host
- Access to an openpilot or comma device on the same network

### Installation

- **Windows:** [Download the latest release](https://github.com/spektor56/OpenpilotToolkit/releases/download/1.9.8/OpenpilotToolkit.zip), extract the archive, and launch `OpenpilotToolkit.exe`.
- **Android:** [Download the APK](https://github.com/spektor56/OpenpilotToolkit/releases/download/1.9.5/com.spektor56.openpilottoolkitandroid.apk) and sideload it on your device.
- **Linux/macOS:** Build and run the CLI locally:

```bash
dotnet build OpenpilotToolkit.Cli/OpenpilotToolkit.Cli.csproj
dotnet run --project OpenpilotToolkit.Cli -- discover
```

## Usage

1. Connect your comma device and ensure it is on the same network as the computer or phone running OPTK.
2. Launch the Windows app, Android app, or the CLI host.
3. Use the SSH wizard on Windows/Android if needed, or point the CLI at an existing host key with `--ssh-key`.
4. Explore the drive exporter, fork installer, remote controls, and route tools as needed.

CLI examples:

```bash
dotnet run --project OpenpilotToolkit.Cli -- discover
dotnet run --project OpenpilotToolkit.Cli -- routes --host 192.168.1.10 --limit 5
dotnet run --project OpenpilotToolkit.Cli -- install-fork --host 192.168.1.10 --owner commaai --branch master
```

Current migration status:
- The Windows desktop GUI is still WinForms-only.
- The new CLI is the first cross-platform host and is intended to work on Linux now and macOS next.
- SSH key generation is still platform-specific and can be ported later.

Refer to the screenshots below for examples of the available tools.

## Screenshots

![openpilot Toolkit Exporter](https://i.imgur.com/GAG527Q.png)
![openpilot Toolkit Remote](https://i.imgur.com/eog5Bhp.png)
![openpilot Toolkit Explorer](https://i.imgur.com/DkBxWfU.png)
![openpilot Toolkit Fingerprint Wizard](https://i.imgur.com/Nq1dW2k.png)
![openpilot Toolkit SSH Wizard](https://i.imgur.com/9nQLkxy.png)
![openpilot Toolkit Fork Installer](https://i.imgur.com/Qp5pQlK.png)
![openpilot Toolkit Terminal](https://i.imgur.com/3MVi4b9.png)

## Development

Developers can clone the repository and use the scripts in the `.agent/` directory to set up dependencies and build the Windows/Android solution:

```bash
git clone https://github.com/spektor56/OpenpilotToolkit.git
cd OpenpilotToolkit
git submodule update --init --recursive
bash .agent/setup.sh
bash .agent/quick-build.sh
```

For the cross-platform host, the smaller entry point is:

```bash
dotnet build OpenpilotToolkit.Cli/OpenpilotToolkit.Cli.csproj
dotnet run --project OpenpilotToolkit.Cli -- --help
```

## Contributing

Contributions are welcome! Please feel free to open an issue or submit a pull request.

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Support / Donate

If you find this project helpful, consider supporting its development:

<a href='https://ko-fi.com/M4M55991G' target='_blank'><img alt="Ko-Fi donation link" height='36' style='border:0px;height:36px;' src='https://cdn.ko-fi.com/cdn/kofi1.png?v=2' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

<a href="https://www.buymeacoffee.com/spektor56"><img alt="buy me a coffee donation link" src="https://img.buymeacoffee.com/button-api/?text=Buy me a coffee&emoji=&slug=spektor56&button_colour=5F7FFF&font_colour=ffffff&font_family=Cookie&outline_colour=000000&coffee_colour=FFDD00"></a>
