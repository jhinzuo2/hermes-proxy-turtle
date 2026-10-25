# Item icons for Turtle displays: Turtle's ItemDisplayInfo.dbc gives display -> icon name; displays the proxy CSV
# already maps teach icon name -> 1.14 FileDataID (majority vote; the TBC CSV fills names the 1.14 CSV lacks).
# Writes "display,fileDataId" rows for displays missing from ItemDisplayIdToFileDataId1.csv; append them to it.
# -NoIcon: items_no_icon.csv from the dbscan run, to report how many "?" items get fixed.
param([string]$Dbc, [string]$CsvDir, [string]$NoIcon, [string]$OutCsv)
. (Join-Path $PSScriptRoot 'dbc.ps1')
$d = Read-Dbc $Dbc
$icon = @{}
for ($r = 0; $r -lt $d.Count; $r++) {
  $icon[[uint32](Get-DbcU32 $d $r 0)] = (Get-DbcStr $d $r 5).ToLowerInvariant()
}
function Load-Map($path) {
  $m = @{}
  foreach ($line in (Get-Content $path | Select-Object -Skip 1)) { $p = $line.Split(','); $m[[uint32]$p[0]] = [uint32]$p[1] }
  $m
}
$known = Load-Map (Join-Path $CsvDir 'ItemDisplayIdToFileDataId1.csv')
$tbc = Load-Map (Join-Path $CsvDir 'ItemDisplayIdToFileDataId2.csv')

# icon name -> FileDataID by majority vote over displays the 1.14 CSV maps
function Learn($maps) {
  $votes = @{}
  foreach ($m in $maps) {
    foreach ($k in $m.Keys) {
      if (-not $icon.ContainsKey($k)) { continue }
      $n = $icon[$k]; if (-not $n) { continue }
      if (-not $votes.ContainsKey($n)) { $votes[$n] = @{} }
      $votes[$n][$m[$k]] = 1 + [int]$votes[$n][$m[$k]]
    }
  }
  $res = @{}
  foreach ($n in $votes.Keys) { $res[$n] = ($votes[$n].GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key }
  $res
}
$byName = Learn @($known)
"learned from 1.14 csv: $($byName.Count) icon names"

# does Turtle number its TBC-era displays like TBC? compare where both sources give an answer
$agree = 0; $disagree = 0
foreach ($k in $tbc.Keys) {
  if ($known.ContainsKey($k) -or -not $icon.ContainsKey($k)) { continue }
  $n = $icon[$k]; if (-not $n -or -not $byName.ContainsKey($n)) { continue }
  if ($byName[$n] -eq $tbc[$k]) { $agree++ } else { $disagree++ }
}
"TBC csv vs learned names on displays missing from 1.14 csv: agree $agree, disagree $disagree"
$byName2 = Learn @($known, $tbc)
"learned incl. TBC csv: $($byName2.Count)"

$rows = Import-Csv $NoIcon
foreach ($set in @(@{N='1.14 only'; M=$byName}, @{N='with TBC'; M=$byName2})) {
  $ok = 0; $miss = New-Object System.Collections.Generic.List[string]
  foreach ($row in $rows) {
    $disp = [uint32]$row.display
    if ($icon.ContainsKey($disp) -and $set.M.ContainsKey($icon[$disp])) { $ok++ } else { $miss.Add("$disp=$($icon[$disp])") }
  }
  "$($set.N): scan no-icon items resolvable $ok / $($rows.Count)"
}
"still missing: " + (($miss | Select-Object -Unique) -join '; ')

$added = New-Object System.Collections.Generic.List[string]
foreach ($k in ($icon.Keys | Sort-Object)) {
  if ($known.ContainsKey($k)) { continue }
  $n = $icon[$k]
  if (-not $n) { continue }
  if ($byName.ContainsKey($n)) { $added.Add("$k,$($byName[$n])") }
  elseif ($byName2.ContainsKey($n)) { $added.Add("$k,$($byName2[$n])") }
}
"rows to add: $($added.Count)"
$added | Set-Content $OutCsv
