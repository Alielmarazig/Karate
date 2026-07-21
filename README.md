# 🥋 Karate — Software Update Monitor

A modern Windows app inspired by SUMo (KC Softwares, discontinued 2023). Scans your
installed software and tells you which apps have updates available.

## How it works

**Applications tab**
1. **Scan** — enumerates installed software from the Windows registry uninstall keys
   (HKLM 64-bit, HKLM 32-bit/WOW6432Node, and HKCU) plus Microsoft Store / MSIX
   packages, filtering out system components, hotfixes, and child entries.
2. **Check for Updates** — runs `winget upgrade` in the background, parses the results,
   and matches them against the scanned apps. Apps with updates sort to the top and
   counter cards show updates / up-to-date / total at a glance.

**Drivers tab**
1. **Scan** — enumerates installed device drivers via WMI (`Win32_PnPSignedDriver`).
2. **Check for Updates** — queries the Windows Update Agent COM API for available
   driver updates (`Type='Driver'`) and matches them to devices by hardware ID.
   Installation is left to Windows Update itself (one click away).

## Tech stack

- **C# / .NET 9** (WPF, `net9.0-windows`, `RollForward=LatestMajor` so it also runs on the .NET 10 runtime)
- **[WPF-UI](https://github.com/lepoco/wpfui) 4.x** — Fluent / Windows 11 design: Mica backdrop,
  dark & light theme following the system setting
- **CommunityToolkit.Mvvm** — MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`)

## Project layout

| Path | Purpose |
|---|---|
| `Models/UpdatableItem.cs` | Shared base: update status, sort rank, status colors |
| `Models/InstalledApp.cs`, `Models/DriverInfo.cs` | App / driver models |
| `Services/RegistryScanner.cs` | Registry-based installed-software enumeration |
| `Services/StoreAppScanner.cs` | Microsoft Store / MSIX package enumeration |
| `Services/WingetService.cs` | Runs and parses `winget upgrade` |
| `Services/DriverScanner.cs` | WMI driver enumeration (`Win32_PnPSignedDriver`) |
| `Services/DriverUpdateService.cs` | Windows Update Agent COM search for driver updates |
| `ViewModels/` | Per-tab commands, counters, search & filtering |
| `MainWindow.xaml` | Fluent UI: tabs, toolbars, counter cards, data grids |

## Build & run

```powershell
dotnet build
dotnet run
```

Requires Windows 10/11 with winget (App Installer) for update checking; scanning works without it.

## Installer (MSI)

The MSI is built with the [WiX Toolset](https://wixtoolset.org) v5 (free, MS-RL licensed — v7 requires
accepting the OSMF EULA). The app is published self-contained, so end users do **not** need .NET installed.

```powershell
.\build-installer.ps1    # output: installer\Karate-<version>-x64.msi
```

The installer ([installer/Karate.wxs](installer/Karate.wxs)) is per-machine (x64, Program Files),
adds Start Menu + Desktop shortcuts, shows a license/install-dir wizard (WixUI_InstallDir), and
supports clean major upgrades — keep the `UpgradeCode` GUID unchanged forever, bump `Version` on
each release.

> **Note for internet distribution:** the MSI is unsigned, so users will see a Windows SmartScreen
> warning. To remove it you need an Authenticode code-signing certificate (OV/EV) and `signtool`.

## Roadmap ideas

- MSIX / Microsoft Store app enumeration (`Get-AppxPackage`)
- Portable-app detection via PE version info of EXEs in chosen folders
- One-click update (`winget upgrade --id <id>`)
- App icons in the list (extract from `DisplayIcon`)
- Scheduled background checks + toast notifications
