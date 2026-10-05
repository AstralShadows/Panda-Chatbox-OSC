# Panda-Chatbox-OSC

Panda Chatbox is a Windows WPF application for sending customizable status messages to the VRChat chatbox.

The interface and application orchestration are written in C#. A native C++ DLL handles hardware sampling, OSC networking, Windows desktop information, status templates, and text styles. Inno Setup creates the Windows installer.

Credit for the original idea and UI inspiration goes to Boihanny.

## Build

Requirements:
- .NET 8 SDK
- Visual Studio 2022 with **Desktop development with C++**
- Windows 10 or Windows 11 SDK
- Inno Setup 6

From the repository root, build and publish the app and its native DLL with:

```powershell
dotnet run --project tools/PandaChatbox.Build -- publish
```

To create the all-in-one Inno Setup installer:

```powershell
dotnet run --project tools/PandaChatbox.Build -- installer
```

The installer executable is written to `installer-output\PandaChatbox-Setup.exe`. It bundles the published application and its native DLL and installs for the current Windows user with Start menu/optional Desktop shortcuts and an uninstaller. The application and installer both use `favicon.ico` as their icon. The build tool automatically locates Inno Setup 6 in its default install locations; if installed elsewhere, set `INNO_SETUP_COMPILER` to the full path of `ISCC.exe`.

## Release notifications

The app checks the public GitHub Releases page at startup and every six hours while open. When a newer stable version is available, a separate update window appears with options to view the release, close it for now, or permanently disable update popups. The update notice below the VRChat send controls can still be used to open the release page. Updates are not downloaded or installed automatically.
