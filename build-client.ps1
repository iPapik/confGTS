$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\ConfGTS.Client\ConfGTS.Client.csproj"
$out = Join-Path $root "artifacts\publish"

& (Join-Path $root "prepare-assets.ps1")

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

dotnet publish $project -c Release -r win-x64 --self-contained true -o $out -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Unpackaged WinUI 3 requires the Visual C++ runtime. Deploy it app-local so
# the MSI is self-sufficient even on PCs without the system VC++ Redistributable.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found; cannot collect VC++ app-local runtime." }

$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Redist.14.Latest -property installationPath
if ([string]::IsNullOrWhiteSpace($vsPath)) {
    $vsPath = & $vswhere -latest -products * -property installationPath
}
if ([string]::IsNullOrWhiteSpace($vsPath)) { throw "Visual Studio installation not found." }

$redistRoot = Join-Path $vsPath "VC\Redist\MSVC"
$crtDir = Get-ChildItem $redistRoot -Directory -ErrorAction Stop |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "x64\Microsoft.VC143.CRT" } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1

if (-not $crtDir) { throw "Microsoft.VC143.CRT x64 app-local folder not found under $redistRoot" }
Copy-Item (Join-Path $crtDir "*.dll") $out -Force
Write-Host "Bundled VC++ app-local runtime from: $crtDir" -ForegroundColor Cyan

$requiredCrt = @("vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll")
foreach ($dll in $requiredCrt) {
    if (-not (Test-Path (Join-Path $out $dll))) { throw "Required app-local runtime missing: $dll" }
}

Write-Host "Client: $(Join-Path $out 'ConfGTS.Client.exe')" -ForegroundColor Green
