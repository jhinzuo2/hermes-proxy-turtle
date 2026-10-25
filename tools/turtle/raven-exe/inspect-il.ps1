# Show which Settings field (ReportedBuild / ServerBuild) SendAuthResponse loads, by token, in an extracted
# HermesProxy.dll. Needs System.Reflection.Metadata (PowerShell 7, or run via the .NET SDK).
# Usage: .\inspect-il.ps1 -Dll "<dir>\HermesProxy.dll"
param([Parameter(Mandatory)][string]$Dll)
Add-Type -AssemblyName System.Reflection.Metadata
$fs = [System.IO.File]::OpenRead($Dll)
$pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
$mr = $pe.GetMetadataReader()

# 1) Find Settings type fields ReportedBuild and ServerBuild -> tokens
$settingsFields = @{}
foreach ($td in $mr.TypeDefinitions) {
    $t = $mr.GetTypeDefinition($td)
    $name = $mr.GetString($t.Name)
    if ($name -eq 'Settings') {
        foreach ($fh in $t.GetFields()) {
            $fd = $mr.GetFieldDefinition($fh)
            $fname = $mr.GetString($fd.Name)
            $tok = [System.Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($fh)
            $settingsFields[$fname] = $tok
        }
    }
}
Write-Output ("ReportedBuild token = 0x{0:X8}" -f $settingsFields['ReportedBuild'])
Write-Output ("ServerBuild   token = 0x{0:X8}" -f $settingsFields['ServerBuild'])

# 2) Find method SendAuthResponse, read its IL, list ldsfld(0x7E)+next opcode
foreach ($mdh in $mr.MethodDefinitions) {
    $md = $mr.GetMethodDefinition($mdh)
    $mname = $mr.GetString($md.Name)
    if ($mname -eq 'SendAuthResponse') {
        $rva = $md.RelativeVirtualAddress
        if ($rva -eq 0) { continue }
        $body = $pe.GetMethodBody($rva)
        $il = $body.GetILBytes()
        Write-Output "--- SendAuthResponse IL length $($il.Length) ---"
        for ($i = 0; $i -lt $il.Length; $i++) {
            if ($il[$i] -eq 0x7E) {
                $tok = [System.BitConverter]::ToInt32($il, $i + 1)
                $next = if ($i + 5 -lt $il.Length) { $il[$i+5] } else { 0 }
                Write-Output ("  offset {0}: ldsfld 0x{1:X8}  next-opcode=0x{2:X2}" -f $i, $tok, $next)
            }
        }
    }
}
$pe.Dispose(); $fs.Close()
