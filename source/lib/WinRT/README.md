# Windows Toast compile-time references

These two unmodified Windows Runtime metadata files are extracted from the official Microsoft.Windows.SDK.Contracts NuGet package, version 10.0.19041.1:

- `Windows.Foundation.FoundationContract.winmd` — FoundationContract 4.0.0.0
- `Windows.Foundation.UniversalApiContract.winmd` — UniversalApiContract 10.0.0.0

Package source: https://www.nuget.org/packages/Microsoft.Windows.SDK.Contracts/10.0.19041.1

Exact download: https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.contracts/10.0.19041.1/microsoft.windows.sdk.contracts.10.0.19041.1.nupkg

Copyright Microsoft Corporation. All rights reserved. These files are governed by the Microsoft Windows SDK license, not the Windows Community Toolkit MIT license. The package's original `Microsoft.Windows.SDK.Contracts.nuspec` is retained here with its license URL, https://aka.ms/WinSDKLicenseURL. The license document retrieved from that URL is preserved as `Windows-SDK-LICENSE.rtf`.

The metadata enables the .NET Framework compiler to resolve strongly typed notification settings and failure events. It is included for offline source compilation only and must not be copied beside the executable as a runtime implementation. Windows 10 22H2 and Windows 11 supply the APIs at runtime.

The build scripts also reference `System.Runtime.dll`, `System.Runtime.WindowsRuntime.dll`, and `System.Runtime.InteropServices.WindowsRuntime.dll` from the installed .NET Framework; they do not redistribute those system assemblies.
