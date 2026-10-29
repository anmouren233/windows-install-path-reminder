@echo off
setlocal
pushd "%~dp0"
if not exist "build" mkdir "build"
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /out:build\InstallPathReminder.exe /win32manifest:src\app.manifest /win32icon:src\app.ico /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Xml.dll /reference:lib\Microsoft.Toolkit.Uwp.Notifications.dll src\*.cs
if errorlevel 1 goto failed
copy /y "src\App.config" "build\InstallPathReminder.exe.config" >nul
copy /y "lib\Microsoft.Toolkit.Uwp.Notifications.dll" "build\Microsoft.Toolkit.Uwp.Notifications.dll" >nul
if errorlevel 1 goto failed
echo Build succeeded: build\InstallPathReminder.exe
popd
exit /b 0
:failed
echo Build failed.
popd
exit /b 1
