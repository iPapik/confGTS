#requires -version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RecordingDirectory,
    [ValidateSet(0,1)][int]$AutoStartService = 1
)
$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:ProgramData 'ConfGTS'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logPath = Join-Path $logDir 'installer-storage.log'
try {
    $target = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($RecordingDirectory))
    if (-not [IO.Path]::IsPathRooted($target)) { throw "Recording directory must be absolute." }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $configPath = Join-Path $logDir 'config.json'
    $defaultDir = Join-Path $logDir 'Recordings'
    $previous = $defaultDir
    $root = New-Object psobject
    if (Test-Path -LiteralPath $configPath -PathType Leaf) {
        $raw = [IO.File]::ReadAllText($configPath)
        if (-not [string]::IsNullOrWhiteSpace($raw)) { $root = $raw | ConvertFrom-Json }
        $old = $root.recording_dir
        if (-not $old) { $old = $root.recordings_path }
        if ($old) { $previous = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables([string]$old)) }
    }
    $service = Get-Service -Name 'ConfGTSServer' -ErrorAction Stop
    if ($service.Status -eq 'Running') {
        Stop-Service -Name 'ConfGTSServer' -Force -ErrorAction Stop
        (Get-Service -Name 'ConfGTSServer').WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    try {
        # Keep old recordings and never overwrite a newer file at the destination.
        if ((Test-Path -LiteralPath $previous -PathType Container) -and
            -not [string]::Equals($previous.TrimEnd('\'), $target.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            Get-ChildItem -LiteralPath $previous -File -Recurse -ErrorAction Stop | ForEach-Object {
                $relative = $_.FullName.Substring($previous.TrimEnd('\').Length).TrimStart('\')
                $destination = Join-Path $target $relative
                $parent = Split-Path -Parent $destination
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
                if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
                    Copy-Item -LiteralPath $_.FullName -Destination $destination -ErrorAction Stop
                }
            }
        }
        $root | Add-Member -NotePropertyName 'recording_dir' -NotePropertyValue $target -Force
        if ($root.PSObject.Properties['recordings_path']) {
            $root.PSObject.Properties.Remove('recordings_path')
        }
        $tempConfig = $configPath + '.installer.tmp'
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $json = $root | ConvertTo-Json -Depth 64
        [IO.File]::WriteAllText($tempConfig, $json, $utf8NoBom)
        if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination ($configPath + '.installer.bak') -Force }
        Move-Item -LiteralPath $tempConfig -Destination $configPath -Force
        Set-Service -Name 'ConfGTSServer' -StartupType $(if ($AutoStartService -eq 1) {'Automatic'} else {'Manual'})
    }
    finally {
        Start-Service -Name 'ConfGTSServer' -ErrorAction Stop
    }
    ('[{0}] OK recording_dir={1} autostart={2}' -f (Get-Date), $target, $AutoStartService) |
        Out-File -FilePath $logPath -Encoding UTF8 -Append
    exit 0
}
catch {
    ('[{0}] ERROR {1}' -f (Get-Date), ($_ | Out-String)) |
        Out-File -FilePath $logPath -Encoding UTF8 -Append
    Write-Error $_
    exit 1
}
