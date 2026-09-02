# Strips "_ = " discard prefix on lines that match the pattern.
# Preserves leading whitespace. Inner "out _" patterns stay intact
# because the regex anchors at line start.

param(
    [string[]]$Paths = @(),
    [switch]$DryRun
)

$totalLinesStripped = 0
$totalFiles = 0
$files = if ($Paths.Count -gt 0) { $Paths } else {
    git grep -lE "^\s*_\s*=\s" -- "platform/src/**" 2>$null | Where-Object { $_ -notmatch "Migrations/" }
}

foreach ($file in $files) {
    $content = Get-Content -LiteralPath $file -Raw
    $original = $content

    $regex = [regex]'(?m)^([ \t]*)_\s*=\s'
    $matches = $regex.Matches($content)
    if ($matches.Count -eq 0) {
        continue
    }

    $stripped = $regex.Replace($content, '$1')
    if ($DryRun) {
        Write-Host "DRY: $file -> $($matches.Count) lines stripped"
        $totalLinesStripped += $matches.Count
        $totalFiles++
        continue
    }

    Set-Content -LiteralPath $file -Value $stripped -NoNewline
    Write-Host "FIX: $file -> $($matches.Count) lines stripped"
    $totalLinesStripped += $matches.Count
    $totalFiles++
}

Write-Host ""
Write-Host "Total: $totalFiles files, $totalLinesStripped lines stripped"
