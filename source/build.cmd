@echo off
setlocal
pushd "%~dp0"
if not exist "build" mkdir "build"
set "NOTIFICATION_FRAMEWORK=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "NOTIFICATION_RUNTIME=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll"
if not exist "%NOTIFICATION_RUNTIME%" set "NOTIFICATION_RUNTIME=%NOTIFICATION_FRAMEWORK%\Facades\System.Runtime.dll"
if not exist "%NOTIFICATION_RUNTIME%" goto failed
"%NOTIFICATION_FRAMEWORK%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /out:build\InstallPathReminder.exe /win32manifest:src\app.manifest /win32icon:src\app.ico /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Xml.dll /reference:lib\Microsoft.Toolkit.Uwp.Notifications.dll /reference:lib\WinRT\Windows.Foundation.FoundationContract.winmd /reference:lib\WinRT\Windows.Foundation.UniversalApiContract.winmd /reference:"%NOTIFICATION_RUNTIME%" /reference:"%NOTIFICATION_FRAMEWORK%\System.Runtime.WindowsRuntime.dll" /reference:"%NOTIFICATION_FRAMEWORK%\System.Runtime.InteropServices.WindowsRuntime.dll" src\*.cs
if errorlevel 1 goto failed
copy /y "src\App.config" "build\InstallPathReminder.exe.config" >nul
if errorlevel 1 goto failed
copy /y "lib\Microsoft.Toolkit.Uwp.Notifications.dll" "build\Microsoft.Toolkit.Uwp.Notifications.dll" >nul
if errorlevel 1 goto failed
echo Build succeeded: build\InstallPathReminder.exe
popd
exit /b 0
:failed
echo Build failed.
popd
exit /b 1
