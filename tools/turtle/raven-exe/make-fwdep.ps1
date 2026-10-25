# Turn an extracted (self-contained) HermesProxy folder into a framework-dependent one: delete the BCL dlls the
# installed runtime provides and rewrite runtimeconfig.json with rollForward, so `dotnet HermesProxy.dll` runs.
# Usage: .\make-fwdep.ps1 -Run "<extracted dir>" -Shared "<dotnet>\shared\Microsoft.NETCore.App\<version>"
param([Parameter(Mandatory)][string]$Run, [Parameter(Mandatory)][string]$Shared)
$run = $Run; $shared = $Shared
$sharedNames = @{}
Get-ChildItem $shared -Filter *.dll | ForEach-Object { $sharedNames[$_.Name] = $true }
# assemblies to always keep (the app itself)
$keep = @{ 'HermesProxy.dll' = $true; 'Framework.dll' = $true }
$removed = 0; $kept = @()
foreach ($dll in Get-ChildItem $run -Filter *.dll) {
    if ($keep.ContainsKey($dll.Name)) { $kept += $dll.Name; continue }
    if ($sharedNames.ContainsKey($dll.Name)) { Remove-Item $dll.FullName -Force; $removed++ }
    else { $kept += $dll.Name }
}
Write-Output "removed $removed BCL dlls provided by shared runtime"
Write-Output "kept (app + third-party): $($kept.Count)"
$kept | Sort-Object | ForEach-Object { "  $_" }
# rewrite runtimeconfig.json as framework-dependent with roll-forward
$rc = @'
{
  "runtimeOptions": {
    "tfm": "net6.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "6.0.0"
    },
    "rollForward": "LatestMajor",
    "configProperties": {
      "System.Reflection.Metadata.MetadataUpdater.IsSupported": false
    }
  }
}
'@
Set-Content -Path "$run\HermesProxy.runtimeconfig.json" -Value $rc -Encoding UTF8
Write-Output "runtimeconfig rewritten framework-dependent (rollForward LatestMajor)"
