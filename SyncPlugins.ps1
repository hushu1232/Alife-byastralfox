param(
    [Parameter(Mandatory=$true)]
    [string]$OutputsDir,

    [Parameter(Mandatory=$true)]
    [string]$NuGetDir
)

$clientDir = Join-Path $OutputsDir "Alife.Client"
$functionDirs = Get-ChildItem $OutputsDir -Directory | Where-Object { $_.Name -match '^Alife\.Function\.' }

foreach ($funcDir in $functionDirs) {
    $files = Get-ChildItem $funcDir.FullName -Recurse -File
    foreach ($file in $files) {
        if ($file.Name -match '^Alife\.Function\.') { continue }
        if (Test-Path (Join-Path $clientDir $file.Name)) { continue }

        $relativePath = $file.FullName.Substring($funcDir.FullName.Length + 1)
        $destPath = Join-Path $NuGetDir $relativePath
        $destDir = Split-Path $destPath -Parent

        if (!(Test-Path $destDir)) {
            New-Item $destDir -ItemType Directory -Force | Out-Null
        }

        Copy-Item $file.FullName $destPath -Force
        Write-Host "  [sync] $relativePath"
    }
}
