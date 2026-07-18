$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'Test-QZoneRealRuntime.ps1'

function Assert-Equal($Actual, $Expected) {
    if ($Actual -ne $Expected) { throw "Expected '$Expected', got '$Actual'." }
}

function Assert-True($Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

function Invoke-Operator([string[]]$Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) }
}

$default = Invoke-Operator @('-Operation', 'Read', '-Port', '3001')
Assert-Equal $default.ExitCode 0
$defaultResult = ($default.Output -join [Environment]::NewLine) | ConvertFrom-Json
Assert-Equal $defaultResult.operation 'Read'
Assert-Equal $defaultResult.port 3001
Assert-Equal $defaultResult.execute $false
Assert-True ($defaultResult.message -match 'Add -Execute') 'Default preview must explain that execution was not requested.'

$deletePreview = Invoke-Operator @('-Operation', 'Delete', '-Port', '3001')
Assert-Equal $deletePreview.ExitCode 0
$deletePreviewResult = ($deletePreview.Output -join [Environment]::NewLine) | ConvertFrom-Json
Assert-Equal $deletePreviewResult.operation 'Delete'
Assert-Equal $deletePreviewResult.port 3001
Assert-Equal $deletePreviewResult.execute $false
Assert-True ($deletePreviewResult.message -match 'Add -Execute') 'Delete preview must remain inert until execution is explicitly requested.'

$deleteUnavailable = Invoke-Operator @('-Operation', 'Delete', '-Port', '3001', '-Execute')
Assert-Equal $deleteUnavailable.ExitCode 1
$deleteUnavailableResult = ($deleteUnavailable.Output -join [Environment]::NewLine) | ConvertFrom-Json
Assert-Equal $deleteUnavailableResult.operation 'Delete'
Assert-Equal $deleteUnavailableResult.port 3001
Assert-Equal $deleteUnavailableResult.execute $true
Assert-Equal $deleteUnavailableResult.message 'local_qzone_runtime_unavailable'

$unavailable = Invoke-Operator @('-Operation', 'Read', '-Port', '3002', '-Execute')
Assert-Equal $unavailable.ExitCode 1
$unavailableResult = ($unavailable.Output -join [Environment]::NewLine) | ConvertFrom-Json
Assert-Equal $unavailableResult.operation 'Read'
Assert-Equal $unavailableResult.port 3002
Assert-Equal $unavailableResult.execute $true
Assert-Equal $unavailableResult.message 'local_qzone_runtime_unavailable'

$missingImage = Invoke-Operator @('-Operation', 'Image', '-Port', '3001', '-Execute', '-ImagePath', 'C:\not-a-real-qzone-image.png')
Assert-Equal $missingImage.ExitCode 1
$missingImageResult = ($missingImage.Output -join [Environment]::NewLine) | ConvertFrom-Json
Assert-Equal $missingImageResult.message 'local_qzone_image_path_unavailable'
Assert-True (($missingImage.Output -join [Environment]::NewLine) -notmatch 'not-a-real-qzone-image') 'Image-path failure must not echo the caller path.'

$source = Get-Content -LiteralPath $scriptPath -Raw
Assert-True ($source -match 'ALIFE_ACCOUNT_A_ONEBOT_TOKEN') 'Account A token variable name is required.'
Assert-True ($source -match 'ALIFE_ACCOUNT_B_ONEBOT_TOKEN') 'Account B token variable name is required.'
Assert-True ($source -notmatch '(?i)Invoke-WebRequest|Invoke-RestMethod|HttpClient|ClientWebSocket|Start-Process') 'Operator script must not open HTTP/WebSocket connections or start a runtime.'
