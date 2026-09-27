# WCAG contrast checker + auto-proposal for theme primitives.
# Computes contrast ratios for muted/faint text against every surface
# (including tinted status backgrounds) per theme × mode.
#
# Modes:
#   pwsh -File scripts\a11y-contrast-check.ps1              # current state + violations
#   pwsh -File scripts\a11y-contrast-check.ps1 -ProposeFix   # print proposed primitives

param([switch]$ProposeFix)

$ErrorActionPreference = "Stop"

function HexToRgb([string]$hex) {
    $h = $hex.TrimStart('#')
    return @{
        r = [int]([convert]::ToInt32($h.Substring(0, 2), 16))
        g = [int]([convert]::ToInt32($h.Substring(2, 2), 16))
        b = [int]([convert]::ToInt32($h.Substring(4, 2), 16))
    }
}
function ChannelToLinear([int]$c) {
    $s = $c / 255.0
    if ($s -le 0.03928) { return $s / 12.92 }
    return [math]::Pow(($s + 0.055) / 1.055, 2.4)
}
function Luminance([hashtable]$rgb) {
    $r = ChannelToLinear $rgb.r
    $g = ChannelToLinear $rgb.g
    $b = ChannelToLinear $rgb.b
    return 0.2126 * $r + 0.7152 * $g + 0.0722 * $b
}
function Contrast([string]$fg, [string]$bg) {
    $la = Luminance (HexToRgb $fg)
    $lb = Luminance (HexToRgb $bg)
    $lighter = [math]::Max($la, $lb)
    $darker = [math]::Min($la, $lb)
    return ($lighter + 0.05) / ($darker + 0.05)
}
function MixOklabHex([string]$fg, [string]$bg, [double]$opacity) {
    $frgb = HexToRgb $fg
    $brgb = HexToRgb $bg
    $r = [int][math]::Round($frgb.r * $opacity + $brgb.r * (1 - $opacity))
    $g = [int][math]::Round($frgb.g * $opacity + $brgb.g * (1 - $opacity))
    $b = [int][math]::Round($frgb.b * $opacity + $brgb.b * (1 - $opacity))
    return ('#{0:X2}{1:X2}{2:X2}' -f $r, $g, $b)
}
function RgbToHex([int]$r, [int]$g, [int]$b) {
    return ('#{0:X2}{1:X2}{2:X2}' -f $r, $g, $b)
}
function IsLight([string]$hex) {
    return (Luminance (HexToRgb $hex)) -gt 0.5
}

# All 7 themes (mirror of themes.ts).
$themes = [ordered]@{
    "dichromat-deck" = @{
        dark  = @{ floor = "#222226"; rail = "#2b2b30"; lane = "#26262b"; laneAlt = "#232327"; raised = "#313136"; hover = "#313136"; text = "#e8e8ee"; muted = "#b8b8bd"; faint = "#8a8a8f"; running = "#8787f3"; queued = "#a2a28a"; waiting = "#b4b442"; escalated = "#b7b7fd"; failed = "#d2d228"; success = "#d7d7ff"; cancelled = "#ebebcf" }
        light = @{ floor = "#e9e9f0"; rail = "#e3e3e9"; lane = "#f5f5fb"; laneAlt = "#e8e8ee"; raised = "#ffffff"; hover = "#e8e8ee"; text = "#1f1f24"; muted = "#414147"; faint = "#67676c"; running = "#5353d7"; queued = "#595949"; waiting = "#4d4d01"; escalated = "#2424b0"; failed = "#333311"; success = "#0d0d7d"; cancelled = "#1b1b07" }
    }
    "graphite" = @{
        dark  = @{ floor = "#0d0f13"; rail = "#1a1d20"; lane = "#14171a"; laneAlt = "#101317"; raised = "#1f2326"; hover = "#1f2326"; text = "#e4e8ed"; muted = "#adb1b6"; faint = "#7a7d81"; running = "#6cb2ff"; queued = "#888f95"; waiting = "#ffdaac"; escalated = "#d9c6ff"; failed = "#f77671"; success = "#75d0ae"; cancelled = "#eceef2" }
        light = @{ floor = "#f3f7fc"; rail = "#e8edf2"; lane = "#fcfeff"; laneAlt = "#eef2f7"; raised = "#feffff"; hover = "#eef2f7"; text = "#1c2023"; muted = "#42464a"; faint = "#6c7073"; running = "#00579e"; queued = "#676d73"; waiting = "#4f3000"; escalated = "#512f7e"; failed = "#951720"; success = "#006f53"; cancelled = "#272a2d" }
    }
    "dockside" = @{
        dark  = @{ floor = "#1c1209"; rail = "#2e1e0f"; lane = "#22170e"; laneAlt = "#1e150b"; raised = "#31261c"; hover = "#31261c"; text = "#ebe5df"; muted = "#b7b0aa"; faint = "#867e76"; running = "#83aefe"; queued = "#968d85"; waiting = "#ffd9b2"; escalated = "#ebbfff"; failed = "#f47b61"; success = "#99cb8e"; cancelled = "#f2ede9" }
        light = @{ floor = "#fdefe1"; rail = "#f4dfcb"; lane = "#fffaf5"; laneAlt = "#f8eadc"; raised = "#fffdfc"; hover = "#f8eadc"; text = "#25211c"; muted = "#4a443e"; faint = "#736b63"; running = "#2e549c"; queued = "#736a63"; waiting = "#512f00"; escalated = "#5d2b72"; failed = "#921e05"; success = "#3d6b33"; cancelled = "#2e2a25" }
    }
    "blueprint" = @{
        dark  = @{ floor = "#061629"; rail = "#0b213b"; lane = "#0c1b2f"; laneAlt = "#08182b"; raised = "#17273c"; hover = "#17273c"; text = "#e1e6ec"; muted = "#abb3bc"; faint = "#78828e"; running = "#9bd6f6"; queued = "#948d87"; waiting = "#ffd9ac"; escalated = "#cb99f7"; failed = "#fc7460"; success = "#62d1ae"; cancelled = "#ebeef2" }
        light = @{ floor = "#e7f2ff"; rail = "#d4e7ff"; lane = "#f7fbff"; laneAlt = "#e0eeff"; raised = "#ffffff"; hover = "#e0eeff"; text = "#1a1d22"; muted = "#3e4349"; faint = "#676d75"; running = "#005799"; queued = "#716b65"; waiting = "#4f3100"; escalated = "#5a297e"; failed = "#980e04"; success = "#006f55"; cancelled = "#272a2f" }
    }
    "bureau" = @{
        dark  = @{ floor = "#1b1a18"; rail = "#22221f"; lane = "#1e1e1b"; laneAlt = "#191816"; raised = "#272624"; hover = "#272624"; text = "#e6e6e2"; muted = "#b4b4b0"; faint = "#858481"; running = "#66b3ff"; queued = "#8a8e93"; waiting = "#ffdba3"; escalated = "#dbc5ff"; failed = "#f77769"; success = "#89ce92"; cancelled = "#ededeb" }
        light = @{ floor = "#f3f3ef"; rail = "#eaeae6"; lane = "#fffffe"; laneAlt = "#f9f8f4"; raised = "#fffffe"; hover = "#f9f8f4"; text = "#1e1e1b"; muted = "#444441"; faint = "#6e6e6a"; running = "#005899"; queued = "#696c70"; waiting = "#4b3200"; escalated = "#532e7d"; failed = "#951815"; success = "#286e37"; cancelled = "#2a2a28" }
    }
    "aperture" = @{
        dark  = @{ floor = "#282d31"; rail = "#2f3337"; lane = "#0e1215"; laneAlt = "#0a0d11"; raised = "#35393e"; hover = "#35393e"; text = "#e9eef4"; muted = "#bcc1c6"; faint = "#90959a"; running = "#76b0ff"; queued = "#878f96"; waiting = "#ffdaa6"; escalated = "#dec3ff"; failed = "#ff7166"; success = "#71d19c"; cancelled = "#eaeef4" }
        light = @{ floor = "#d5dae0"; rail = "#e0e6eb"; lane = "#feffff"; laneAlt = "#f4faff"; raised = "#feffff"; hover = "#f4faff"; text = "#1a1d22"; muted = "#393c42"; faint = "#5b5e64"; running = "#0954a8"; queued = "#666c74"; waiting = "#4c3100"; escalated = "#582982"; failed = "#9b040f"; success = "#006f44"; cancelled = "#272a2f" }
    }
    "dispatcher" = @{
        dark  = @{ floor = "#0c0f13"; rail = "#171c24"; lane = "#11151a"; laneAlt = "#0e1216"; raised = "#1a1f27"; hover = "#14191f"; text = "#dee4ea"; muted = "#aaafb4"; faint = "#787d82"; running = "#3c5a86"; queued = "#a0a4ac"; waiting = "#9c7a3c"; escalated = "#6e5ba6"; failed = "#b0473b"; success = "#4e7c5b"; cancelled = "#8e9195" }
        light = @{ floor = "#fbfbfa"; rail = "#f0f1ee"; lane = "#f1f2ef"; laneAlt = "#f7f8f6"; raised = "#ffffff"; hover = "#ecedea"; text = "#1b232e"; muted = "#434952"; faint = "#6e7379"; running = "#3c5a86"; queued = "#a0a4ac"; waiting = "#9c7a3c"; escalated = "#6e5ba6"; failed = "#b0473b"; success = "#4e7c5b"; cancelled = "#8e9195" }
    }
}

$TINT_OPACITY = 0.18
$surfaceKeys = @("floor","rail","lane","laneAlt","raised","hover")
$statusKeys = @("running","queued","waiting","escalated","failed","success","cancelled")

function AllBackgrounds([hashtable]$p) {
    # Returns list of {key, hex} for every surface AND every status-tinted background.
    $bg = @()
    foreach ($s in $surfaceKeys) { $bg += @{ key = $s; hex = $p[$s] } }
    foreach ($st in $statusKeys) { $bg += @{ key = "tint-$st"; hex = (MixOklabHex $p[$st] $p.lane $TINT_OPACITY) } }
    return $bg
}

function WorstContrast([string]$fg, [hashtable]$p) {
    $bgList = AllBackgrounds $p
    $worst = 99
    $worstBg = ""
    foreach ($bg in $bgList) {
        $c = Contrast $fg $bg.hex
        if ($c -lt $worst) {
            $worst = $c
            $worstBg = "$($bg.key) ($($bg.hex))"
        }
    }
    return @{ ratio = $worst; bg = $worstBg }
}

function ProposePaletteFix([hashtable]$p) {
    # We need text/muted/faint to clear 4.5:1 against every surface and tinted
    # background, while preserving the design system invariant that the text
    # ladder (text → muted → faint) is at least 12 L* per rung.
    #
    # Dark mode: shift the entire ladder toward white by the delta faint needs
    # to clear AA on the worst tinted bg. Gaps preserved, ladder shape intact.
    #
    # Light mode: skipped in this pass — the design invariant (≥12 L* per rung,
    # even ladder) cannot be reconciled with AA on tinted backgrounds without
    # collapsing the ladder onto black. Documented as forward debt; light-mode
    # tinted-bg violations remain in a11y-known-issues.json.
    $origText = HexToRgb $p.text
    $origMuted = HexToRgb $p.muted
    $origFaint = HexToRgb $p.faint
    $textLum = Luminance $origText
    $floorLum = Luminance (HexToRgb $p.floor)
    $isDark = $textLum -gt $floorLum
    if (-not $isDark) {
        return @{ text = $p.text; muted = $p.muted; faint = $p.faint; alpha = 0; skipped = $true }
    }
    $targetRgb = @{ r = 255; g = 255; b = 255 }
    $lo = 0.0; $hi = 1.0; $alpha = 1.0
    for ($i = 0; $i -lt 50; $i++) {
        $mid = ($lo + $hi) / 2
        $r = [int][math]::Round($origFaint.r * (1 - $mid) + $targetRgb.r * $mid)
        $g = [int][math]::Round($origFaint.g * (1 - $mid) + $targetRgb.g * $mid)
        $b = [int][math]::Round($origFaint.b * (1 - $mid) + $targetRgb.b * $mid)
        $faintCand = RgbToHex $r $g $b
        $w = WorstContrast $faintCand $p
        if ($w.ratio -ge 4.5) {
            $alpha = $mid
            $hi = $mid
        } else {
            $lo = $mid
        }
    }
    function Apply($orig, $a) {
        $r = [int][math]::Round($orig.r * (1 - $a) + $targetRgb.r * $a)
        $g = [int][math]::Round($orig.g * (1 - $a) + $targetRgb.g * $a)
        $b = [int][math]::Round($orig.b * (1 - $a) + $targetRgb.b * $a)
        $r = [math]::Max(0, [math]::Min(255, $r))
        $g = [math]::Max(0, [math]::Min(255, $g))
        $b = [math]::Max(0, [math]::Min(255, $b))
        return RgbToHex $r $g $b
    }
    return @{
        text  = Apply $origText  $alpha
        muted = Apply $origMuted $alpha
        faint = Apply $origFaint $alpha
        alpha = $alpha
        skipped = $false
    }
}

function ReportTheme([string]$tid, [hashtable]$t) {
    foreach ($mode in @("dark","light")) {
        $p = $t[$mode]
        Write-Host ""
        Write-Host "=== $tid · $mode ===" -ForegroundColor Cyan
        foreach ($tk in @("muted","faint")) {
            $w = WorstContrast $p[$tk] $p
            $status = if ($w.ratio -lt 4.5) { "FAIL" } else { "ok" }
            $color = if ($status -eq "FAIL") { "Red" } else { "Green" }
            Write-Host ("  {0,-5} {1,-6}  current={2}  worst={3:F2} on {4}" -f $tk, $status, $p[$tk], $w.ratio, $w.bg) -ForegroundColor $color
        }
        if ($ProposeFix) {
            $fix = ProposePaletteFix $p
            Write-Host ("  propose: text={0} muted={1} faint={2}  (alpha={3:F3})" -f $fix.text, $fix.muted, $fix.faint, $fix.alpha) -ForegroundColor Yellow
            # Verify the proposed palette clears all bg.
            $vp = @{ text=$fix.text; muted=$fix.muted; faint=$fix.faint } + @{ }
            foreach ($k in $p.Keys) { $vp[$k] = $p[$k] }
            foreach ($k in @("muted","faint","text")) {
                $w = WorstContrast $vp[$k] $vp
                Write-Host ("    verify: {0,-5} worst={1:F2} on {2}" -f $k, $w.ratio, $w.bg) -ForegroundColor Green
            }
        }
    }
}

function ReportTheme([string]$tid, [hashtable]$t) {
    foreach ($mode in @("dark","light")) {
        $p = $t[$mode]
        Write-Host ""
        Write-Host "=== $tid · $mode ===" -ForegroundColor Cyan
        foreach ($tk in @("muted","faint")) {
            $w = WorstContrast $p[$tk] $p
            $status = if ($w.ratio -lt 4.5) { "FAIL" } else { "ok" }
            $color = if ($status -eq "FAIL") { "Red" } else { "Green" }
            Write-Host ("  {0,-5} {1,-6}  current={2}  worst={3:F2} on {4}" -f $tk, $status, $p[$tk], $w.ratio, $w.bg) -ForegroundColor $color
        }
        if ($ProposeFix) {
            $fix = ProposePaletteFix $p
            Write-Host ("  propose: text={0} muted={1} faint={2}  (alpha={3:F3})" -f $fix.text, $fix.muted, $fix.faint, $fix.alpha) -ForegroundColor Yellow
            # Verify: build a candidate palette with ONLY text/muted/faint replaced.
            $vp = @{}
            foreach ($k in $p.Keys) { $vp[$k] = $p[$k] }
            $vp.text  = $fix.text
            $vp.muted = $fix.muted
            $vp.faint = $fix.faint
            foreach ($k in @("muted","faint","text")) {
                $w = WorstContrast $vp[$k] $vp
                Write-Host ("    verify: {0,-5} worst={1:F2} on {2}" -f $k, $w.ratio, $w.bg) -ForegroundColor Green
            }
        }
    }
}

foreach ($tid in $themes.Keys) {
    ReportTheme $tid $themes[$tid]
}
