# Diagnose ConfGTS startup resource usage without administrator rights.
# Run once with ConfGTS closed and once while the symptom is present.
[CmdletBinding()]
param([ValidateRange(3,60)][int]$Seconds=10)

$ErrorActionPreference='Stop'
$logDir=Join-Path $env:LOCALAPPDATA 'ConfGTS\logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$report=Join-Path $logDir ("startup-resources-$stamp.csv")
$info=Join-Path $logDir ("startup-resources-$stamp.txt")
$names=@('ConfGTS.Client','msedgewebview2','audiodg','chrome','msedge','firefox')
$cpuCount=[Math]::Max(1,[Environment]::ProcessorCount)

function Snapshot {
    $items=@{}
    foreach($p in (Get-Process -Name $names -ErrorAction SilentlyContinue)) {
        $cpu=0.0
        try { if ($null -ne $p.CPU) { $cpu=[double]$p.CPU } } catch {}
        $items[[string]$p.Id]=[pscustomobject]@{
            Name=$p.ProcessName
            PID=$p.Id
            CPU=$cpu
            RAMMB=[Math]::Round($p.WorkingSet64/1MB,1)
            Handles=$p.Handles
        }
    }
    return $items
}
$a=Snapshot
Start-Sleep -Seconds $Seconds
$b=Snapshot
$rows=foreach($item in $b.Values) {
    $before=$a[[string]$item.PID]
    $delta=if($before){[Math]::Max(0,$item.CPU-$before.CPU)}else{0}
    [pscustomobject]@{
        Process=$item.Name
        PID=$item.PID
        CPUPercentOfMachine=[Math]::Round(100*$delta/$Seconds/$cpuCount,1)
        RAMMB=$item.RAMMB
        Handles=$item.Handles
        StartedDuringSample=($null -eq $before)
    }
}
$rows | Sort-Object CPUPercentOfMachine -Descending |
    Export-Csv -LiteralPath $report -Delimiter ';' -NoTypeInformation -Encoding UTF8
$rows | Sort-Object CPUPercentOfMachine -Descending |
    Format-Table -AutoSize | Out-String -Width 160 |
    Out-File -LiteralPath $info -Encoding UTF8
Write-Host "Saved: $report"
Write-Host "Summary: $info"
Write-Host "NOTE: the script does not access or modify audio devices."
