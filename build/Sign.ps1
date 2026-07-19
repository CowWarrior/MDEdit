param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath
)

$ErrorActionPreference = 'Stop'

# Windows Kits version folders vary by machine/SDK updates, so search rather than hardcode.
$signTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object -Property { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $signTool) {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { $signTool = $cmd.Source }
}

if (-not $signTool) {
    Write-Error "signtool.exe not found. Install the Windows SDK (Windows Kits 10) or put signtool.exe on PATH."
    exit 1
}

# /n matches by subject name and picks the most valid match, so this survives the cert's yearly rotation
# without needing a thumbprint update.
& $signTool sign /n "Maze Code Signing" /fd SHA256 /tr "http://timestamp.digicert.com" /td SHA256 "$FilePath"
exit $LASTEXITCODE
