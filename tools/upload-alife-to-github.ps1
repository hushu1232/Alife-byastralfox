param(
    [string]$RepoRoot = "D:\Alife",
    [string]$Remote = "alife-byastralfox",
    [string]$Branch = "master"
)

$ErrorActionPreference = "Stop"

$AllowedRemoteUrls = @(
    "git@github.com:hushu1232/Alife-byastralfox.git",
    "https://github.com/hushu1232/Alife-byastralfox",
    "https://github.com/hushu1232/Alife-byastralfox.git"
)

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

function Invoke-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }

    return ($output -join "`n").Trim()
}

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot ".git"))) {
    throw "RepoRoot is not a git repository: $RepoRoot"
}

$currentBranch = Invoke-GitOutput @("-C", $RepoRoot, "rev-parse", "--abbrev-ref", "HEAD")
if ($currentBranch -ne $Branch) {
    throw "Upload must run from branch '$Branch'. Current branch is '$currentBranch'."
}

$pushUrl = Invoke-GitOutput @("-C", $RepoRoot, "remote", "get-url", "--push", $Remote)
if ($AllowedRemoteUrls -notcontains $pushUrl) {
    throw "Remote '$Remote' points to '$pushUrl'. Expected Alife-byastralfox only."
}

$status = Invoke-GitOutput @("-C", $RepoRoot, "status", "--porcelain")
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Working tree is not clean. Commit or remove local changes before upload."
}

$localCommit = Invoke-GitOutput @("-C", $RepoRoot, "rev-parse", "HEAD")

Write-Host "Uploading $localCommit from $RepoRoot to $Remote/$Branch..."
Invoke-Git @("-C", $RepoRoot, "push", $Remote, "HEAD:$Branch")

Write-Host "Verifying remote branch..."
$remoteRef = Invoke-GitOutput @("-C", $RepoRoot, "ls-remote", "--heads", $Remote, $Branch)
if ([string]::IsNullOrWhiteSpace($remoteRef)) {
    throw "Remote branch verification failed: refs/heads/$Branch was not found."
}

if (-not $remoteRef.StartsWith($localCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Remote verification failed. Expected $localCommit but got: $remoteRef"
}

Write-Host "Verified $Remote/$Branch at $localCommit."
