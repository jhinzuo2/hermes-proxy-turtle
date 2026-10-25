function Read-Dbc([string]$path) {
  $b = [IO.File]::ReadAllBytes($path)
  $n = [BitConverter]::ToInt32($b,4); $f = [BitConverter]::ToInt32($b,8); $rs = [BitConverter]::ToInt32($b,12); $ss = [BitConverter]::ToInt32($b,16)
  $strBase = 20 + $n*$rs
  [pscustomobject]@{ Bytes=$b; Count=$n; Fields=$f; RecSize=$rs; StrBase=$strBase }
}
function Get-DbcU32($d, [int]$rec, [int]$field) { [BitConverter]::ToUInt32($d.Bytes, 20 + $rec*$d.RecSize + $field*4) }
function Get-DbcStr($d, [int]$rec, [int]$field) {
  $off = Get-DbcU32 $d $rec $field
  $start = $d.StrBase + $off; $end = $start
  while ($end -lt $d.Bytes.Length -and $d.Bytes[$end] -ne 0) { $end++ }
  [Text.Encoding]::UTF8.GetString($d.Bytes, $start, $end-$start)
}
