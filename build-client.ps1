$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\ConfGTS.Client\ConfGTS.Client.csproj"
$out = Join-Path $root "artifacts\publish"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

dotnet publish $project -c Release -r win-x64 --self-contained true -o $out -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Client: $(Join-Path $out 'ConfGTS.Client.exe')" -ForegroundColor Green
