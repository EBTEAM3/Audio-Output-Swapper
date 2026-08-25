<#
.SYNOPSIS
Reports the memory footprint of AudioSwapper and only its own child processes.

Filtering by process name alone is wrong on a normal Windows machine: plenty of
other apps host WebView2, so a bare "msedgewebview2" count sweeps up hundreds of
megabytes that have nothing to do with this app. This walks the parent chain
instead.
#>
param([string]$ProcessName = 'AudioSwapper')

$all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name

$roots = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
if ($roots.Count -eq 0) { "not running"; return }

# Breadth-first walk down the tree from each root.
$tree = New-Object System.Collections.Generic.HashSet[int]
$queue = New-Object System.Collections.Generic.Queue[int]
foreach ($r in $roots) { [void]$tree.Add($r); $queue.Enqueue($r) }

while ($queue.Count -gt 0) {
    $pid_ = $queue.Dequeue()
    foreach ($child in ($all | Where-Object { $_.ParentProcessId -eq $pid_ })) {
        if ($tree.Add([int]$child.ProcessId)) { $queue.Enqueue([int]$child.ProcessId) }
    }
}

$rows = foreach ($id in $tree) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p) {
        [pscustomobject]@{
            Name       = $p.ProcessName
            Id         = $p.Id
            WS_MB      = [math]::Round($p.WorkingSet64 / 1MB, 1)
            Private_MB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        }
    }
}

$rows | Sort-Object WS_MB -Descending | Format-Table -AutoSize | Out-String | Write-Output
"processes: {0}   total WS: {1} MB   total private: {2} MB" -f
    @($rows).Count,
    [math]::Round((($rows | Measure-Object WS_MB -Sum).Sum), 1),
    [math]::Round((($rows | Measure-Object Private_MB -Sum).Sum), 1)
