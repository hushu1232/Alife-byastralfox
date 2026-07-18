param(
    [ValidateSet('Read','Post','Comment','Like','Image')]
    [string]$Operation = 'Read',
    [ValidateSet(3001,3002)]
    [int]$Port,
    [switch]$Execute,
    [string]$ImagePath
)

$ErrorActionPreference = 'Stop'

function Write-QZoneOperatorResult {
    param(
        [bool]$Succeeded,
        [string]$Message
    )

    [ordered]@{
        operation = $Operation
        port = $Port
        execute = $true
        success = $Succeeded
        message = $Message
    } | ConvertTo-Json -Compress
}

if (-not $Execute) {
    [ordered]@{
        operation = $Operation
        port = $Port
        execute = $false
        message = 'Add -Execute to call the selected real QZone operation.'
    } | ConvertTo-Json -Compress
    exit 0
}

if ($Port -notin @(3001, 3002)) {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_account_port_invalid'
    exit 1
}

if ($Operation -eq 'Image' -and ([string]::IsNullOrWhiteSpace($ImagePath) -or -not (Test-Path -LiteralPath $ImagePath -PathType Leaf))) {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_image_path_unavailable'
    exit 1
}

# Map the selected account to its established OneBot token variable name. This script intentionally
# does not read or print the variable value: the current checkout has no local QZone operator endpoint
# through which to pass it, and the script must never invent an endpoint or start a QQ/NapCat runtime.
$tokenEnvironmentVariableName = if ($Port -eq 3001) {
    'ALIFE_ACCOUNT_A_ONEBOT_TOKEN'
}
else {
    'ALIFE_ACCOUNT_B_ONEBOT_TOKEN'
}

# Keep the selected name local until an already-running, explicitly configured local operator endpoint exists.
$null = $tokenEnvironmentVariableName
Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_runtime_unavailable'
exit 1
