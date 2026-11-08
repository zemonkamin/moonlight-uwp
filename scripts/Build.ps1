param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('ARM','x86','x64')][string]$Platform = 'ARM',
    [switch]$Package,
    [string]$SigningCertificate
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$install = & $vswhere -version '[15.0,16.0)' -products '*' -latest -property installationPath
if (!$install) { throw 'Visual Studio 2017 with UWP and C++ v141 tools is required.' }
$msbuild = Join-Path $install 'MSBuild/15.0/Bin/MSBuild.exe'
Push-Location $root
try {
    & "$PSScriptRoot/Bootstrap.ps1"
    New-Item -ItemType Directory -Force build | Out-Null
    $arguments = @('Moonlight.sln','/restore','/m:4',"/p:Configuration=$Configuration", "/p:Platform=$Platform", '/v:minimal', '/fl', "/flp:logfile=build/$Configuration-$Platform.log;verbosity=normal;encoding=UTF-8")
    if ($Package) { $arguments += @('/p:GenerateAppxPackageOnBuild=true', '/p:UapAppxPackageBuildMode=SideloadOnly') }
    if ($SigningCertificate) {
        $arguments += @('/p:AppxPackageSigningEnabled=true', "/p:PackageCertificateKeyFile=$((Resolve-Path -LiteralPath $SigningCertificate).Path)")
    }
    & $msbuild @arguments
    if ($LASTEXITCODE) { throw "Build failed. See build/$Configuration-$Platform.log" }
}
finally { Pop-Location }
