[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).ProviderPath
if (!(Test-Path -LiteralPath (Join-Path $root 'Moonlight.sln'))) {
    throw 'Run this helper from a complete Moonlight source checkout.'
}
$workspacePrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$relativeTargets = @(
    '.vs', 'artifacts', 'bin', 'obj', 'build',
    'Moonlight/AppPackages', 'Moonlight/bin', 'Moonlight/obj',
    'native/Generated Files',
    'Moonlight/Moonlight.csproj.user', 'native/Moonlight.Native.vcxproj.user',
    'Moonlight/Moonlight_TemporaryKey.pfx'
)
$failures = [Collections.Generic.List[string]]::new()
foreach ($relativeTarget in $relativeTargets) {
    $target = [IO.Path]::GetFullPath((Join-Path $root $relativeTarget))
    if (!$target.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target is outside the source checkout: $target"
    }
    if (!(Test-Path -LiteralPath $target)) { continue }
    try {
        $item = Get-Item -LiteralPath $target -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Cleanup does not follow symbolic links or directory junctions.'
        }
        if ($item.PSIsContainer) {
            $link = Get-ChildItem -LiteralPath $target -Recurse -Force |
                Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
                Select-Object -First 1
            if ($link) { throw "Cleanup target contains a symbolic link or junction: $($link.FullName)" }
        }
        if ($PSCmdlet.ShouldProcess($target, 'Remove generated files or local development settings')) {
            Remove-Item -LiteralPath $target -Recurse -Force
            Write-Output "Removed: $relativeTarget"
        }
    }
    catch {
        $message = '{0}: {1}' -f $relativeTarget, $_.Exception.Message
        $failures.Add($message)
        Write-Warning $message
    }
}
if ($failures.Count) {
    throw ('Cleanup incomplete. Close Visual Studio or other processes holding generated files and retry. ' + ($failures -join '; '))
}
