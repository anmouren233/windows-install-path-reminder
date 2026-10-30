param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 的 .NET Framework 4.x C# 编译器。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sourceDirectory = Join-Path $PSScriptRoot 'src'
$sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | ForEach-Object { $_.FullName })
$exe = Join-Path $OutputDirectory 'InstallPathReminder.exe'
$options = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', ('/out:' + $exe), ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll', '/reference:System.Xml.dll')
$options += ('/win32icon:' + (Join-Path $sourceDirectory 'app.ico'))
$notifications = Join-Path $PSScriptRoot 'lib\Microsoft.Toolkit.Uwp.Notifications.dll'
. (Join-Path $PSScriptRoot 'compiler-references.ps1')
$options += @(Get-NotificationCompilerReferences -ProjectRoot $PSScriptRoot)
& $compiler @options @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'App.config') -Destination ($exe + '.config') -Force
Copy-Item -LiteralPath $notifications -Destination $OutputDirectory -Force
Write-Output $exe
