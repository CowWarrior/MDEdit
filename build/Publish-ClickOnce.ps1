$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$csproj = Join-Path $repoRoot 'MDEdit\MDEdit.csproj'
$pubxml = Join-Path $repoRoot 'MDEdit\Properties\PublishProfiles\ClickOnce.pubxml'

# dotnet publish's cross-platform MSBuild can't run ClickOnce's UpdateManifest task (MSB4803),
# so this must go through the full-framework MSBuild that ships with Visual Studio. Search rather
# than hardcode the VS version/edition, since that varies by machine.
$msbuild = Get-ChildItem -Path @(
        "${env:ProgramFiles}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\amd64\MSBuild.exe"
    ) -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $msbuild) {
    Write-Error "Full-framework MSBuild.exe not found. ClickOnce publish requires Visual Studio (or Build Tools) to be installed."
    exit 1
}

# ClickOnce manifest signing requires RSA and matches by thumbprint (unlike signtool's /n subject
# match), so resolve the current cert's thumbprint at publish time rather than storing it anywhere.
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=Maze Code Signing' -and $_.NotAfter -gt (Get-Date) -and $_.PublicKey.Oid.FriendlyName -eq 'RSA' } |
    Sort-Object NotBefore -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Error "No valid RSA certificate with subject 'CN=Maze Code Signing' found in CurrentUser\My. ClickOnce manifest signing requires RSA."
    exit 1
}

# Ties the ClickOnce revision to repo history so it's always increasing without persisting state anywhere.
$revision = (git -C $repoRoot rev-list --count HEAD).Trim()

Write-Output "Restoring win-x64 assets..."
& dotnet restore $csproj -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Publishing ClickOnce build (revision $revision, cert $($cert.Thumbprint))..."
& $msbuild $csproj -t:Publish -p:PublishProfile=$pubxml -p:ApplicationRevision=$revision -p:ManifestCertificateThumbprint=$($cert.Thumbprint)
exit $LASTEXITCODE
