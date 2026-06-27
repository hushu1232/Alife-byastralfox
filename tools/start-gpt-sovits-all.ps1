$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $Root "start-gpt-sovits-xiayu.ps1")
& (Join-Path $Root "start-gpt-sovits-mixu.ps1")
