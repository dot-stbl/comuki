# self-audit.ps1
# Scans the last commit's changed .cs files for banned patterns from
# .agents/rules/process/worker-audit.md and writes a markdown report.
#
# Exits 0 even when violations are found (advisory only -- VerifyRuleAwareness
# is "WARNING ONLY" per the design). The MSBuild target emits the count as a
# build log Message with a [WARN] prefix; TreatWarningsAsErrors does not
# promote MSBuild <Message> output, so the build still succeeds.
#
# Patterns checked (banned under the project's C# rules):
#   - _ = discard of awaited expression         (async-and-tasks.md sec.6)
#   - .ConfigureAwait(...)                      (async-and-tasks.md sec.3)
#   - ArgumentException.ThrowIf* / ArgumentNullException.ThrowIf*
#                                                (code-shape.md sec.11)
#   - private static * JsonSerializerOptions    (anti-patterns.md sec.6)
#   - throw <identifier>; (resets stack)        (exceptions.md sec.5)
#
# Excluded paths (tool-generated, not hand-written):
#   - **/Migrations/**
#   - **/*.Designer.cs
#   - **/*ModelSnapshot.cs

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Output,

    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [Parameter(Mandatory = $false)]
    [string]$CommitRef = 'HEAD'
)

$ErrorActionPreference = 'SilentlyContinue'
$nl = [Environment]::NewLine

$patterns = @(
    @{ Id = 'discard';        Label = '_ = discard';                  Regex = '_ = ' },
    @{ Id = 'configureawait'; Label = 'ConfigureAwait';               Regex = '\.ConfigureAwait\(' },
    @{ Id = 'throwif';        Label = 'ThrowIf';                      Regex = 'Argument(Null|Exception|OutOfRangeException)\.ThrowIf' },
    @{ Id = 'jsonopts';       Label = 'static JsonSerializerOptions'; Regex = 'private\s+static[^=]*JsonSerializerOptions' },
    @{ Id = 'throwex';        Label = 'throw ex';                     Regex = 'throw\s+[A-Za-z_][A-Za-z0-9_]*\s*;' }
)

Push-Location -LiteralPath $RepoRoot
try {
    $commitSha = (& git rev-parse $CommitRef) 2>$null
    if ([string]::IsNullOrEmpty($commitSha)) {
        $report = @(
            '# Last-commit self-audit',
            '',
            "**Commit:** $CommitRef (could not resolve)",
            '**Files audited:** 0',
            '**Total violations:** 0',
            '',
            'No commit resolved -- nothing to audit.'
        )
        Set-Content -LiteralPath $Output -Value ($report -join $nl) -Encoding utf8
        Write-Host "[self-audit] no commit resolved; wrote empty report"
        return
    }

    $commitSubject = (& git log -1 --format='%s' $CommitRef) 2>$null

    $hasParent = $true
    & git rev-parse --verify "${CommitRef}~1" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { $hasParent = $false }

    if ($hasParent) {
        $diffArg = @('diff', "${CommitRef}~1..${CommitRef}", '--name-only', '--', '*.cs')
    } else {
        # Initial commit: show files added by HEAD itself.
        $diffArg = @('show', '--name-only', '--format=', $CommitRef, '--', '*.cs')
    }

    $rawFiles = & git @diffArg 2>$null
    $changed = @($rawFiles | Where-Object { $_ -and $_ -notmatch '^[+\-\\]{3}' })

    $scanned = @()
    $hitsByFile = @{}
    $hitsByPattern = @{}

    foreach ($pat in $patterns) { $hitsByPattern[$pat.Id] = 0 }

    foreach ($rel in $changed) {
        $abs = Join-Path $RepoRoot $rel
        if (-not (Test-Path -LiteralPath $abs)) { continue }

        # Exclude tool-generated paths.
        if ($rel -match '[\\/]Migrations[\\/]') { continue }
        if ($rel -match '\.Designer\.cs$') { continue }
        if ($rel -match 'ModelSnapshot\.cs$') { continue }

        $scanned += $rel
        $fileHits = @{}

        foreach ($pat in $patterns) {
            # git grep returns matches per file (path:line:match). Empty result
            # means no hits. -E is extended regex; we want line numbers.
            $matches = & git grep -nE $pat.Regex -- $rel 2>$null
            $lines = @($matches | Where-Object { $_ -and $_.StartsWith($rel) })
            if ($lines.Count -gt 0) {
                $fileHits[$pat.Id] = $lines
                $hitsByPattern[$pat.Id] += $lines.Count
            }
        }

        if ($fileHits.Count -gt 0) {
            $hitsByFile[$rel] = $fileHits
        }
    }

    $totalViolations = ($hitsByPattern.Values | Measure-Object -Sum).Sum

    # Build report.
    $report = New-Object System.Collections.Generic.List[string]
    [void]$report.Add('# Last-commit self-audit')
    [void]$report.Add('')
    [void]$report.Add("- **Commit:** ``$commitSha`` $commitSubject")
    [void]$report.Add("- **Files scanned:** $($scanned.Count) .cs file(s)")
    [void]$report.Add("- **Total violations:** $totalViolations (across $($patterns.Count) patterns)")
    [void]$report.Add('')
    [void]$report.Add('## Pattern tally')
    [void]$report.Add('')
    [void]$report.Add('| Pattern | Rule | Hits |')
    [void]$report.Add('|---------|------|-----:|')
    foreach ($pat in $patterns) {
        $hits = $hitsByPattern[$pat.Id]
        $marker = if ($hits -gt 0) { '!' } else { 'ok' }
        $row = ('| {0} | {1} | {2} {3} |' -f $pat.Id, $pat.Label, $hits, $marker)
        [void]$report.Add($row)
    }
    [void]$report.Add('')
    [void]$report.Add('## Files with hits')
    [void]$report.Add('')
    if ($hitsByFile.Count -eq 0) {
        [void]$report.Add('_None -- clean._')
    }
    else {
        foreach ($rel in ($hitsByFile.Keys | Sort-Object)) {
            $fileHits = $hitsByFile[$rel]
            [void]$report.Add("### ``$rel``")
            foreach ($pat in $patterns) {
                if ($fileHits.ContainsKey($pat.Id)) {
                    [void]$report.Add('')
                    $header = "- **$($pat.Label)** ($($fileHits[$pat.Id].Count) hit(s)):"
                    [void]$report.Add($header)
                    foreach ($line in $fileHits[$pat.Id]) {
                        # Format: "<path>:<ln>:<match>"
                        $parts = $line -split ':', 3
                        if ($parts.Count -ge 3) {
                            $detail = '    - line {0}: `{1}`' -f $parts[1], $parts[2].Trim()
                            [void]$report.Add($detail)
                        } else {
                            [void]$report.Add('    - ' + $line)
                        }
                    }
                }
            }
            [void]$report.Add('')
        }
    }

    $parent = Split-Path -Parent $Output
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Set-Content -LiteralPath $Output -Value ($report -join $nl) -Encoding utf8

    $level = if ($totalViolations -gt 0) { '[WARN]' } else { '[INFO]' }
    Write-Host "[self-audit] $level $totalViolations violation(s) across $($hitsByFile.Count) file(s); report at $Output"
} finally {
    Pop-Location
}
