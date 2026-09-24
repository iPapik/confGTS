$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publish = Join-Path $root "artifacts\publish"
$msiOut = Join-Path $root "artifacts\msi"
$setupOut = Join-Path $root "artifacts\setup"

& (Join-Path $root "build-client.ps1")

if (Test-Path $msiOut) { Remove-Item $msiOut -Recurse -Force }
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $msiOut | Out-Null
New-Item -ItemType Directory -Force -Path $setupOut | Out-Null

dotnet build (Join-Path $root "Installer\ConfGTS.Client.Installer.wixproj") -c Release -p:PublishDir="$publish" -p:OutputPath="$msiOut\"
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

$msi = Get-ChildItem $msiOut -Filter *.msi -Recurse | Select-Object -First 1
if (-not $msi) { throw "MSI not found" }
$finalMsi = Join-Path $msiOut "ConfGTS-0.15.1-x64.msi"
if ($msi.FullName -ne $finalMsi) { Copy-Item $msi.FullName $finalMsi -Force }

dotnet build (Join-Path $root "Installer\Bootstrapper\ConfGTS.Bootstrapper.wixproj") -c Release -p:MsiDir="$msiOut" -p:OutputPath="$setupOut\"
if ($LASTEXITCODE -ne 0) { throw "EXE bootstrapper build failed" }

$exe = Get-ChildItem $setupOut -Filter *.exe -Recurse | Select-Object -First 1
if (-not $exe) { throw "EXE installer not found" }
$finalExe = Join-Path $setupOut "ConfGTS-Setup-0.15.1-x64.exe"
if ($exe.FullName -ne $finalExe) { Copy-Item $exe.FullName $finalExe -Force }

Write-Host "MSI: $finalMsi" -ForegroundColor Green
Write-Host "EXE: $finalExe" -ForegroundColor Green
