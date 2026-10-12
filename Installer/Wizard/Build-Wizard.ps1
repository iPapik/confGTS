param([switch]$Client, [switch]$Server)
$ErrorActionPreference = 'Stop'
if (-not $Client -and -not $Server) { throw 'Specify -Client or -Server.' }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (-not $iscc) {
    foreach ($candidate in @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    )) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { $iscc = $candidate; break }
    }
}
if (-not $iscc) {
    throw 'Inno Setup 6 ISCC.exe missing. Install using: choco install innosetup -y'
}
foreach ($kind in @('Client','Server')) {
    if ((($kind -eq 'Client') -and (-not $Client)) -or (($kind -eq 'Server') -and (-not $Server))) { continue }
    $script = Join-Path $PSScriptRoot ($kind + '.iss')
    Write-Host "Building interactive ConfGTS $kind wizard using $iscc" -ForegroundColor Cyan
    & $iscc $script
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup $kind wizard compilation failed: $LASTEXITCODE" }
}
