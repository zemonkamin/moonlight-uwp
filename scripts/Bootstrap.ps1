$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $lock = Get-Content dependencies.lock.json -Raw | ConvertFrom-Json
    git submodule update --init --recursive
    if ($LASTEXITCODE) { throw 'Submodule download failed.' }
    foreach ($dependency in $lock.submodules) {
        $actual = git -C $dependency.path rev-parse HEAD
        if ($actual -ne $dependency.commit) { throw "Unexpected revision for $($dependency.path): $actual. Expected $($dependency.commit)." }
        if ($dependency.patch) {
            $patch = Join-Path $root $dependency.patch
            $previousErrorPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            git -C $dependency.path apply --reverse --check $patch 2>$null
            $alreadyApplied = $LASTEXITCODE -eq 0
            $ErrorActionPreference = $previousErrorPreference
            if (!$alreadyApplied) {
                git -C $dependency.path apply --check $patch
                if ($LASTEXITCODE) { throw "Cannot apply $patch. Check local changes." }
                git -C $dependency.path apply $patch
                if ($LASTEXITCODE) { throw "Patch failed: $patch" }
            }
        }
    }
    & "$PSScriptRoot/Generate-NativeProject.ps1"
    Write-Host 'Official dependencies verified; UWP patch applied; solution ready.'
}
finally { Pop-Location }
