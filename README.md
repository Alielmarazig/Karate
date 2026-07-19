# 🥋 Karate — Software Update Monitor

A modern Windows app inspired by SUMo (KC Softwares, discontinued 2023). Scans your
installed software and tells you which apps have updates available.

## How it works

1. **Scan** — enumerates installed software from the Windows registry uninstall keys
   (HKLM 64-bit, HKLM 32-bit/WOW6432Node, and HKCU), filtering out system components,
   hotfixes, and child entries of larger installers.
2. **Check for Updates** — runs `winget upgrade` in the background, parses the results,
   and matches them against the scanned apps to flag which ones are outdated.

## Tech stack

- **C# / .NET 9** (WPF, `net9.0-windows`, `RollForward=LatestMajor` so it also runs on the .NET 10 runtime)
- **[WPF-UI](https://github.com/lepoco/wpfui) 4.x** — Fluent / Windows 11 design: Mica backdrop,
  dark & light theme following the system setting
- **CommunityToolkit.Mvvm** — MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`)

## Project layout

| Path | Purpose |
|---|---|
| `Models/InstalledApp.cs` | App model with update status (observable) |
| `Services/RegistryScanner.cs` | Registry-based installed-software enumeration |
| `Services/WingetService.cs` | Runs and parses `winget upgrade` |
| `ViewModels/MainViewModel.cs` | Scan / check-updates commands, search & filtering |
| `MainWindow.xaml` | Fluent UI: toolbar, data grid, status bar |

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
