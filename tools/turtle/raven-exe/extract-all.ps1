# Extract every file of a single-file HermesProxy.exe bundle, so it can run unpacked
# (then see make-fwdep.ps1 to run it on the installed .NET runtime).
# Usage: .\extract-all.ps1 -Exe "<path>\HermesProxy.exe" -OutDir "<dir>"
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$OutDir)
. "$PSScriptRoot\bundle-common.ps1"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$bundle = Read-BundleEntries $Exe
$n = 0
foreach ($e in $bundle.Entries) {
    $dest = Join-Path $OutDir $e.Path
    $destDir = Split-Path $dest -Parent
    if ($destDir -and -not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
    [System.IO.File]::WriteAllBytes($dest, (Get-BundleEntryBytes $bundle $e))
    $n++
}
$bundle.Reader.Close()
Write-Output "extracted $n files to $OutDir"
