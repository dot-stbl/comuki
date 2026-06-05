# Build the worker image and run pi in headless mode with a trivial prompt,
# then assert that the stream-json output contains at least one event line.
#
# Slice 0 step 0: the cheapest way to confirm that pi runs as a headless
# worker inside a container and emits a parseable stream-json feed. If this
# fails, the rest of Slice 0 has no foundation.
#
# Usage (PowerShell, from repo root):
#   cd deploy
#   Copy-Item .env.example .env
#   # fill ANTHROPIC_API_KEY in .env
#   .\scripts\test-pi-headless.ps1
#
# Exit code:
#   0 — at least one stream-json event line observed
#   1 — build failed, run failed, or no event lines in output

$ErrorActionPreference = 'Stop'

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$DeployDir   = Resolve-Path (Join-Path $ScriptDir '..')
$RepoRoot    = Resolve-Path (Join-Path $DeployDir '..')
Set-Location $DeployDir

$envFile = Join-Path $DeployDir '.env'
if (-not (Test-Path $envFile)) {
    Write-Error "deploy/.env not found. Copy .env.example to .env and fill in ANTHROPIC_API_KEY."
    exit 1
}

# Load .env into the current process (simple KEY=VALUE parsing).
Get-Content $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -eq '' -or $line.StartsWith('#')) { return }
    $eq = $line.IndexOf('=')
    if ($eq -lt 1) { return }
    $key = $line.Substring(0, $eq).Trim()
    $val = $line.Substring($eq + 1).Trim().Trim('"').Trim("'")
    Set-Item -Path "Env:$key" -Value $val
}

if ([string]::IsNullOrWhiteSpace($env:ANTHROPIC_API_KEY)) {
    Write-Error "ANTHROPIC_API_KEY is empty in deploy/.env. Slice 0 step 0 needs a real key."
    exit 1
}

$prompt = if ([string]::IsNullOrWhiteSpace($env:PI_TEST_PROMPT)) { 'Say hello in exactly one word' } else { $env:PI_TEST_PROMPT }
$cmd = @('pi', '-p', $prompt, '--output-format', 'stream-json')

Write-Host '▶ Building worker image (comuki/worker:dev) ...'
podman compose --env-file .env --profile worker build worker
if ($LASTEXITCODE -ne 0) { Write-Error "image build failed"; exit 1 }

Write-Host "▶ Running pi in headless mode:"
Write-Host "    prompt: $prompt"
Write-Host "    command: $($cmd -join ' ')"

$output = & podman compose --env-file .env --profile worker run --rm worker @cmd 2>&1
$runExit = $LASTEXITCODE

$output | ForEach-Object { Write-Host "    $_" }

if ($runExit -ne 0) {
    Write-Error "pi exited non-zero ($runExit)"
    exit 1
}

$match = $output | Where-Object { $_ -match '^\{\s*"type"\s*:' } | Select-Object -First 1
if ($match) {
    Write-Host "✓ pi produced stream-json events (matched: $match)"
    exit 0
}

Write-Error "no stream-json event lines matched '^{`"type`":'. expected: pi in headless mode should emit one JSON object per line."
exit 1
