param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'build/signing'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=MoonlightMobile' -FriendlyName 'Moonlight Mobile development' -KeyUsage DigitalSignature -KeyExportPolicy Exportable -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(2) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
# Development key only. This unencrypted PFX stays in ignored build/signing.
$emptyPassword = [System.Security.SecureString]::new()
Export-PfxCertificate -Cert $certificate -FilePath (Join-Path $OutputDirectory 'MoonlightMobile.pfx') -Password $emptyPassword | Out-Null
Export-Certificate -Cert $certificate -FilePath (Join-Path $OutputDirectory 'MoonlightMobile.cer') | Out-Null
Write-Host "Development certificate: $OutputDirectory"
