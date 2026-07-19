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
#
# NOTE: this MSBuild's FormatVersion task ignores the ApplicationRevision parameter entirely (verified
# against Microsoft.Build.Tasks.Core.dll from VS 18 Community — Version="1.0.0" + Revision="7" still
# formats as "1.0.0", not "1.0.0.7"). So the revision is folded into a full 4-part ApplicationVersion
# here instead of being passed as a separate property, which IS passed through as-is.
$revision = (git -C $repoRoot rev-list --count HEAD).Trim()
$applicationVersion = "1.0.0.$revision"

Write-Output "Restoring win-x64 assets..."
& dotnet restore $csproj -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Publishing ClickOnce build (version $applicationVersion, cert $($cert.Thumbprint))..."
& $msbuild $csproj -t:Publish -p:PublishProfile=$pubxml -p:ApplicationVersion=$applicationVersion -p:ManifestCertificateThumbprint=$($cert.Thumbprint)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ClickOnce publish only adds a new version folder under "Application Files" — it never removes the
# ones it supersedes, so without this every publish leaves dead weight behind in the repo.
$currentVersionFolder = "MDEdit_$($applicationVersion -replace '\.', '_')"
$applicationFilesDir = Join-Path $repoRoot 'docs\Application Files'
Get-ChildItem $applicationFilesDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne $currentVersionFolder } |
    ForEach-Object {
        Write-Output "Removing superseded ClickOnce version folder: $($_.Name)"
        Remove-Item $_.FullName -Recurse -Force
    }

exit 0
