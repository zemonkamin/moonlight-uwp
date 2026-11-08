param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Configuration $Configuration
    if ($LASTEXITCODE) { throw 'AppContainer tests failed.' }
    exit 0
}
$packageSuffix = if ($Configuration -eq 'Debug') { '_Debug' } else { '' }
[xml]$sourceManifest = Get-Content (Join-Path $root 'Moonlight/Package.appxmanifest') -Raw
$version = $sourceManifest.Package.Identity.Version
$packageDirectory = Join-Path $root "Moonlight/AppPackages/Moonlight_${version}_x64${packageSuffix}_Test"
$appx = Join-Path $packageDirectory "Moonlight_${version}_x64${packageSuffix}.appx"
if (!(Test-Path -LiteralPath $appx)) { throw 'Build x64 with scripts/Build.ps1 -Platform x64 -Configuration Debug -Package first.' }
$dependencies = @(Get-ChildItem (Join-Path $packageDirectory 'Dependencies/x64') -Filter *.appx | Select-Object -ExpandProperty FullName)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$layout = Join-Path $root ('build/test-runs/' + [guid]::NewGuid().ToString('N'))
[System.IO.Compression.ZipFile]::ExtractToDirectory($appx, $layout)
$manifest = Join-Path $layout 'AppxManifest.xml'
if (!(Test-Path (Join-Path $layout 'MoonlightNative.dll'))) { throw 'Native DLL is missing from the APPX payload.' }
foreach ($dependency in $dependencies) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($dependency)
    try {
        $reader = [System.IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
        try { [xml]$dependencyManifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    $identity = $dependencyManifest.Package.Identity
    $installed = Get-AppxPackage -Name $identity.Name | Where-Object { [version]$_.Version -ge [version]$identity.Version }
    if (!$installed) { Add-AppxPackage -Path $dependency }
}
$previous = Get-AppxPackage -Name Moonlight.Mobile
if ($previous) {
    if (!$previous.IsDevelopmentMode -or !$previous.InstallLocation.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'An existing Moonlight.Mobile outside this workspace must be removed or moved by its owner before testing.'
    }
    Remove-AppxPackage -Package $previous.PackageFullName -PreserveApplicationData
}
Add-AppxPackage -Register $manifest -ForceApplicationShutdown
$package = Get-AppxPackage -Name Moonlight.Mobile
$report = Join-Path $env:LOCALAPPDATA "Packages/$($package.PackageFamilyName)/LocalState/self-test.txt"
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager {
    [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
}
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] class ApplicationActivationManager {}
public static class MoonlightTestActivation {
    public static void Launch(string id) {
        uint pid;
        int result = ((IApplicationActivationManager)new ApplicationActivationManager()).ActivateApplication(id, "--self-test", 0, out pid);
        Marshal.ThrowExceptionForHR(result);
    }
}
'@
[MoonlightTestActivation]::Launch($package.PackageFamilyName + '!App')
$deadline = (Get-Date).AddSeconds(90)
while (!(Test-Path -LiteralPath $report) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
if (!(Test-Path -LiteralPath $report)) { throw "No diagnostic report: $report" }
$text = Get-Content -LiteralPath $report -Raw -Encoding UTF8
$text | Set-Content (Join-Path $root "build/self-test-$Configuration-x64.txt") -Encoding UTF8
Write-Output $text
if ($text -notmatch 'RESULT: PASS') { exit 1 }
