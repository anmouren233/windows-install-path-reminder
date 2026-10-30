# Return compile-time references for Windows Toast APIs. The WinMD files are
# reference metadata only: Windows supplies the implementation at runtime.
function Get-NotificationCompilerReferences {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)
    $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $runtimeCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll')
        (Join-Path $frameworkDirectory 'Facades\System.Runtime.dll')
    )
    $runtimeFacade = $runtimeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $runtimeFacade) { throw 'The .NET Framework 4.8 System.Runtime facade was not found.' }
    $files = @(
        (Join-Path $ProjectRoot 'lib\Microsoft.Toolkit.Uwp.Notifications.dll')
        (Join-Path $ProjectRoot 'lib\WinRT\Windows.Foundation.FoundationContract.winmd')
        (Join-Path $ProjectRoot 'lib\WinRT\Windows.Foundation.UniversalApiContract.winmd')
        $runtimeFacade
        (Join-Path $frameworkDirectory 'System.Runtime.WindowsRuntime.dll')
        (Join-Path $frameworkDirectory 'System.Runtime.InteropServices.WindowsRuntime.dll')
    )
    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Required compiler reference is missing: ' + $file) }
        '/reference:' + $file
    }
}
