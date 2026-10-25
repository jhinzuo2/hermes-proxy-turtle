# GameObject displays for Turtle objects: Turtle adds displays (ids above the 1.12 range) that often point at a
# model file a vanilla display already uses. Writes "TurtleDisplay,ClientDisplay" rows (GameObjectDisplayRemap.csv)
# for every Turtle display whose model file a 1.12 display also uses, so the proxy can send the vanilla id.
# -Vanilla: GameObjectDisplayInfo.dbc from patch.MPQ alone (1.12.1 set); -Turtle: the merged one (extract-dbc.ps1).
param([string]$Vanilla, [string]$Turtle, [string]$OutCsv)
. (Join-Path $PSScriptRoot 'dbc.ps1')
function Models($path) {
  $d = Read-Dbc $path; $m = @{}
  for ($r = 0; $r -lt $d.Count; $r++) { $m[[uint32](Get-DbcU32 $d $r 0)] = (Get-DbcStr $d $r 1).ToLowerInvariant().Replace('/', '\') }
  $m
}
$van = Models $Vanilla; $tur = Models $Turtle
$byModel = @{}
foreach ($k in ($van.Keys | Sort-Object)) { $n = $van[$k]; if ($n -and -not $byModel.ContainsKey($n)) { $byModel[$n] = $k } }
# .mdx and .m2 name the same model
function Key($n) { $n -replace '\.(mdx|mdl|m2)$', '' }
$byKey = @{}; foreach ($n in $byModel.Keys) { $k = Key $n; if (-not $byKey.ContainsKey($k)) { $byKey[$k] = $byModel[$n] } }
$rows = New-Object System.Collections.Generic.List[string]; $miss = 0
foreach ($k in ($tur.Keys | Sort-Object)) {
  if ($van.ContainsKey($k)) { continue }
  $n = $tur[$k]; if (-not $n) { continue }
  $key = Key $n
  if ($byKey.ContainsKey($key)) { $rows.Add("$k,$($byKey[$key])") } else { $miss++ }
}
"vanilla displays: $($van.Count), Turtle displays: $($tur.Count), Turtle-only remappable: $($rows.Count), no vanilla model: $miss"
@('TurtleDisplay,ClientDisplay') + $rows | Set-Content $OutCsv
