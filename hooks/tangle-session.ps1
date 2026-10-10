$ErrorActionPreference = 'Stop'
[Console]::In.ReadToEnd() | & node (Join-Path $PSScriptRoot 'tangle-session.mjs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
