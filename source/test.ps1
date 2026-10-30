param([switch]$SkipProcessTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$results = Join-Path $PSScriptRoot ('test-results\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $results -Force | Out-Null
$src = Join-Path $PSScriptRoot 'src'
$tests = Join-Path $PSScriptRoot 'tests'
$notifications = Join-Path $PSScriptRoot 'lib\Microsoft.Toolkit.Uwp.Notifications.dll'
. (Join-Path $PSScriptRoot 'compiler-references.ps1')
$notificationReferences = @(Get-NotificationCompilerReferences -ProjectRoot $PSScriptRoot)
Copy-Item -LiteralPath $notifications -Destination $results
function Assert-Exit([string]$label) { if ($LASTEXITCODE -ne 0) { throw ($label + ' failed, exit code ' + $LASTEXITCODE) } }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $results 'DetectorTests.exe')) /reference:System.Management.dll (Join-Path $src 'InstallerDetection.cs') (Join-Path $tests 'DetectorTests.cs')
Assert-Exit 'Compile DetectorTests'
& (Join-Path $results 'DetectorTests.exe')
Assert-Exit 'DetectorTests'
$appSources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object { $_.FullName })
foreach ($testName in @('StorageTests','NotificationTests','InstallPathTests','DeliveryIntegrationTests')) {
    & $compiler /nologo /utf8output /target:exe ('/main:' + $testName) ('/out:' + (Join-Path $results ($testName + '.exe'))) @notificationReferences /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Xml.dll @appSources (Join-Path $tests ($testName + '.cs'))
    Assert-Exit ('Compile ' + $testName)
    & (Join-Path $results ($testName + '.exe')) (Join-Path $results ($testName + '-data'))
    Assert-Exit $testName
}
if (-not $SkipProcessTests) {
    # Harmless fixtures only wait, then exit. They do not install anything.
    & $compiler /nologo /utf8output /target:winexe ('/out:' + (Join-Path $results 'Fixture.exe')) (Join-Path $tests 'FakeSetup.cs')
    Assert-Exit 'Compile Fixture'
    & $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $results 'MonitorSmoke.exe')) /reference:System.Management.dll (Join-Path $src 'InstallerDetection.cs') (Join-Path $tests 'MonitorSmoke.cs')
    Assert-Exit 'Compile MonitorSmoke'
    & (Join-Path $results 'MonitorSmoke.exe') $results
    Assert-Exit 'MonitorSmoke'
}
Write-Output ('All requested tests passed. Artifacts: ' + $results)
