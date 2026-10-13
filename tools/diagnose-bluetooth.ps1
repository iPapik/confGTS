# Read-only Bluetooth crash diagnostics. Does not reset or disable devices.
[CmdletBinding()]
param([ValidateRange(10,180)][int]$Minutes=60)
$ErrorActionPreference='Continue'
$folder=Join-Path $env:LOCALAPPDATA 'ConfGTS\logs'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$file=Join-Path $folder ("bluetooth-health-{0}.txt" -f (Get-Date -Format 'yyyyMMdd_HHmmss'))
$since=(Get-Date).AddMinutes(-$Minutes)
"ConfGTS Bluetooth health at $(Get-Date -Format o)" | Out-File $file -Encoding UTF8
"Read-only diagnostic. No drivers or settings were modified." | Out-File $file -Append -Encoding UTF8
"=== Bluetooth devices ===" | Out-File $file -Append
Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue |
  Select-Object Status,Class,FriendlyName,InstanceId |
  Format-Table -Wrap -AutoSize | Out-String -Width 250 | Out-File $file -Append
"=== Relevant recent System events ===" | Out-File $file -Append
Get-WinEvent -FilterHashtable @{LogName='System';StartTime=$since} -ErrorAction SilentlyContinue |
  Where-Object {
    $_.ProviderName -match 'BTH|Bluetooth|USB|Kernel-PnP|DriverFrameworks|UserPnp' -or
    $_.Message -match 'Bluetooth|BTHUSB|BTHLE|BTHENUM|USB.*reset|USB.*disconnect'
  } |
  Select-Object -First 120 TimeCreated,Id,ProviderName,LevelDisplayName,Message |
  Format-List | Out-String -Width 250 | Out-File $file -Append
"=== ConfGTS startup log tail ===" | Out-File $file -Append
$startup=Join-Path $folder 'startup.log'
if(Test-Path $startup) {
  Get-Content $startup -Tail 100 | Out-File $file -Append -Encoding UTF8
}
Write-Host "Diagnostic saved: $file"
Write-Host 'Review before sharing: device identifiers and PC names may be present.'
