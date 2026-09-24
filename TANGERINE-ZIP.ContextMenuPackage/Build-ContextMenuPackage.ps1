param(
    [Parameter(Mandatory = $true)] [string] $ShellExtensionPath,
    [Parameter(Mandatory = $true)] [string] $ContextMenuHostPath,
    [Parameter(Mandatory = $true)] [string] $OutputPackagePath,
    [string] $CertificateThumbprint,
    [string] $PfxPath,
    [string] $PfxPassword,
    [string] $PublicCertificatePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint) -eq [string]::IsNullOrWhiteSpace($PfxPath)) {
    throw 'CTXPB0001: Specify exactly one signing certificate thumbprint or PFX path.' #CTXPB0001
}
if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    $resolvedPfxPath = (Resolve-Path -LiteralPath $PfxPath).Path
    $signingCertificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12FromFile(
        $resolvedPfxPath, $PfxPassword, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
} else {
    $CertificateThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $signingCertificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
}
if (-not $signingCertificate.HasPrivateKey) { throw 'CTXPB0001: The signing certificate has no private key.' } #CTXPB0001

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'bin') -Directory |
    Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')) -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\signtool.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if ($null -eq $sdkVersion) { throw 'CTXPB0002: MakeAppx and SignTool were not found.' } #CTXPB0002

$stagingPath = Join-Path $repositoryRoot ('artifacts\context-menu-package-staging\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stagingPath 'Assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $scriptRoot 'AppxManifest.xml') -Destination $stagingPath
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $ShellExtensionPath).Path -Destination (Join-Path $stagingPath 'TangerineZipContextMenuHost.dll')
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $ContextMenuHostPath).Path -Destination (Join-Path $stagingPath 'ContextMenuHost.exe')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'TANGERINE-ZIP\Resources\TZIP.png') -Destination (Join-Path $stagingPath 'Assets\Logo.png')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'TANGERINE-ZIP\Resources\TZIP.png') -Destination (Join-Path $stagingPath 'Assets\StoreLogo.png')

$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPackagePath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($resolvedOutputPath)) -Force | Out-Null
& (Join-Path $sdkVersion.FullName 'x64\makeappx.exe') pack /d $stagingPath /p $resolvedOutputPath /o
if ($LASTEXITCODE -ne 0) { throw 'CTXPB0003: MakeAppx failed.' } #CTXPB0003
if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    & (Join-Path $sdkVersion.FullName 'x64\signtool.exe') sign /fd SHA256 /f $resolvedPfxPath /p $PfxPassword $resolvedOutputPath
} else {
    & (Join-Path $sdkVersion.FullName 'x64\signtool.exe') sign /fd SHA256 /s My /sha1 $CertificateThumbprint $resolvedOutputPath
}
if ($LASTEXITCODE -ne 0) { throw 'CTXPB0004: SignTool failed.' } #CTXPB0004

if (-not [string]::IsNullOrWhiteSpace($PublicCertificatePath)) {
    $resolvedCertificatePath = [IO.Path]::GetFullPath($PublicCertificatePath)
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($resolvedCertificatePath)) -Force | Out-Null
    Export-Certificate -Cert $signingCertificate -FilePath $resolvedCertificatePath -Force | Out-Null
}
