param(
    [string]$SourceRoot = "D:\Alife",
    [string]$FoxdRoot = "D:\FOXD",
    [string]$Remote = "github",
    [string]$Branch = "master",
    [string]$Subtree = "alife-service",
    [string]$Worktree = "D:\tmp\alife-service-upload",
    [string]$CommitMessage = "Update Alife service snapshot",
    [switch]$KeepWorktree
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Assert-InsidePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Child,
        [Parameter(Mandatory = $true)]
        [string]$Parent
    )

    $childFull = Resolve-FullPath $Child
    $parentFull = (Resolve-FullPath $Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$childFull' is not inside '$parentFull'"
    }
}

$SourceRoot = Resolve-FullPath $SourceRoot
$FoxdRoot = Resolve-FullPath $FoxdRoot
$Worktree = Resolve-FullPath $Worktree
$Subtree = $Subtree.Trim("/\")
$DestinationRoot = Join-Path $Worktree $Subtree

if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot ".git"))) {
    throw "SourceRoot is not a git repository: $SourceRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path $FoxdRoot ".git"))) {
    throw "FoxdRoot is not a git repository: $FoxdRoot"
}

Assert-InsidePath -Child $Worktree -Parent "D:\tmp"

Write-Host "Fetching $Remote in $FoxdRoot..."
Invoke-Git @("-C", $FoxdRoot, "fetch", $Remote)

if (Test-Path -LiteralPath $Worktree) {
    Write-Host "Removing existing temporary worktree $Worktree..."
    Invoke-Git @("-C", $FoxdRoot, "worktree", "remove", $Worktree, "--force")
}

$commit = $null
$remoteRef = $null

try {
    Write-Host "Creating temporary worktree from $Remote/$Branch..."
    Invoke-Git @("-C", $FoxdRoot, "worktree", "add", $Worktree, "$Remote/$Branch")

    if (-not (Test-Path -LiteralPath $DestinationRoot)) {
        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
    }

    Write-Host "Collecting tracked source files..."
    $sourceFiles = @(git -c core.quotepath=false -C $SourceRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list tracked source files"
    }

    $sourceSet = New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $sourceFiles) {
        [void]$sourceSet.Add($file.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    }

    Write-Host "Removing target tracked files that no longer exist in source..."
    $targetFiles = @(git -c core.quotepath=false -C $Worktree ls-files $Subtree)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list tracked target files"
    }

    foreach ($targetFile in $targetFiles) {
        $relative = $targetFile.Substring($Subtree.Length).TrimStart("/", "\")
        $relativeForSet = $relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        if (-not $sourceSet.Contains($relativeForSet)) {
            $targetPath = Join-Path $Worktree $targetFile
            Assert-InsidePath -Child $targetPath -Parent $DestinationRoot
            if (Test-Path -LiteralPath $targetPath) {
                Remove-Item -LiteralPath $targetPath -Force
            }
        }
    }

    Write-Host "Copying $($sourceFiles.Count) tracked files into $Subtree..."
    foreach ($file in $sourceFiles) {
        $from = Join-Path $SourceRoot $file
        $to = Join-Path $DestinationRoot $file
        Assert-InsidePath -Child $to -Parent $DestinationRoot
        $parent = Split-Path -Parent $to
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $from -Destination $to -Force
    }

    Write-Host "Staging $Subtree..."
    Invoke-Git @("-C", $Worktree, "add", "-A", "-f", "--", $Subtree)

    $stagedFiles = @(git -c core.quotepath=false -C $Worktree diff --cached --name-only -- $Subtree)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to inspect staged changes"
    }

    if ($stagedFiles.Count -eq 0) {
        Write-Host "No staged changes under $Subtree. Nothing to upload."
        return
    }

    Write-Host "Staged files:"
    foreach ($file in $stagedFiles) {
        Write-Host "  $file"
    }

    Write-Host "Committing snapshot..."
    Invoke-Git @("-C", $Worktree, "commit", "-m", $CommitMessage)

    $commit = (& git -C $Worktree rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        throw "Failed to read committed HEAD"
    }

    Write-Host "Pushing $commit to $Remote/$Branch..."
    Invoke-Git @("-C", $Worktree, "push", $Remote, "HEAD:$Branch")

    Write-Host "Verifying remote branch..."
    $remoteRef = (& git -C $Worktree ls-remote $Remote "refs/heads/$Branch").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteRef)) {
        throw "Failed to verify remote branch"
    }

    if (-not $remoteRef.StartsWith($commit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Remote verification failed. Expected $commit but got $remoteRef"
    }

    Write-Host "Verified remote: $remoteRef"
}
finally {
    if (-not $KeepWorktree -and (Test-Path -LiteralPath $Worktree)) {
        Write-Host "Removing temporary worktree $Worktree..."
        Invoke-Git @("-C", $FoxdRoot, "worktree", "remove", $Worktree, "--force")
    }
}

Write-Host "Upload complete."
