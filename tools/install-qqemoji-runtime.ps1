[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceRoot,

    [string]$OutputDir,

    [string]$StorageRoot
)

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot "..\Outputs"
}

$source = (Resolve-Path -LiteralPath $SourceRoot -ErrorAction Stop).Path
$output = [System.IO.Path]::GetFullPath($OutputDir)
$storage = if ([string]::IsNullOrWhiteSpace($StorageRoot)) {
    Join-Path (Split-Path -Parent $output) "Storage"
}
else {
    [System.IO.Path]::GetFullPath($StorageRoot)
}
$required = @("QQEmoji.cs", "QQEmojiUI_razor.g.cs", "Alife.Plugin.QQEmoji.json")

foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
        throw "QQEmoji source is missing required file: $name"
    }
}
$qqEmojiSource = Join-Path $source "QQEmoji.cs"
if (-not (Select-String -LiteralPath $qqEmojiSource -Pattern "EnableQqEmojiSaveImageCapability" -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -Pattern "SearchEmojis" -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern "QQEmojiExplicitRequestSend" -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern 'ExecuteFunctionAsync("qimage"' -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern "QChatSafeImageDownloader.DownloadAsync" -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern "TryGetSafeImageName" -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern '"tool.qqemoji.save"' -Quiet) -or
    -not (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern "RecordPluginRuntimeAudit" -Quiet) -or
    (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern "BuildEmojiListString" -Quiet) -or
    (Select-String -LiteralPath $qqEmojiSource -SimpleMatch -Pattern ".HandlerTable." -Quiet)) {
    throw "QQEmoji source is missing the required governed Alife integration adaptations."
}

foreach ($pluginDirectory in @("Plugins", "PluginsDebug")) {
    $pluginRoot = Join-Path $storage $pluginDirectory
    $target = Join-Path $pluginRoot "Alife.Plugin.QQEmoji"
    $stage = Join-Path $pluginRoot (".qqemoji-install-" + [guid]::NewGuid().ToString("N"))
    $backup = "$target.backup"

    New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null
    try {
        New-Item -ItemType Directory -Path $stage | Out-Null
        foreach ($name in $required) {
            Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $stage $name) -Force
        }

        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
        if (Test-Path -LiteralPath $target) {
            Move-Item -LiteralPath $target -Destination $backup
        }

        Move-Item -LiteralPath $stage -Destination $target
        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
    }
    catch {
        if (-not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $target
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }

    Write-Host "Installed QQEmoji runtime plugin to $target"
}
