# T-0101 - composite the rendered Pyre frames into labelled contact sheets.
# Unity produced the pixels; this only lays them out and draws the labels.
Add-Type -AssemblyName System.Drawing

$root   = "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0101"
$frames = Join-Path $root "frames"
$strips = Join-Path $root "strips"
$rows   = Get-Content (Join-Path $root "manifest.txt") | Where-Object { $_.Trim().Length -gt 0 }

$entries = @()
foreach ($r in $rows) {
    $c = $r -split '\|'
    $entries += [pscustomobject]@{
        Id=$c[0]; Group=$c[1]; Label=$c[2]; File=$c[3]; Cov=$c[4]; Mean=$c[5]
        Note=$c[7]; Err=$c[8]; Frame=$c[9]; ByFrame=$c[10]
    }
}

$groupColor = @{
    'Enum form'    = [System.Drawing.Color]::FromArgb(255,120,200,255)
    'Plug-in form' = [System.Drawing.Color]::FromArgb(255,255,190,110)
    'Field pass'   = [System.Drawing.Color]::FromArgb(255,150,240,170)
    'Retired slot' = [System.Drawing.Color]::FromArgb(255,190,140,220)
}

function New-Canvas($w, $h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 24, 26, 32))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    return @($bmp, $g)
}

function Draw-Checker($g, $x, $y, $w, $h, $step) {
    $a = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,52,55,64))
    $b = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,42,45,53))
    for ($yy = 0; $yy -lt $h; $yy += $step) {
        for ($xx = 0; $xx -lt $w; $xx += $step) {
            $br = if (((($xx/$step) + ($yy/$step)) % 2) -eq 0) { $a } else { $b }
            $g.FillRectangle($br, $x + $xx, $y + $yy, [math]::Min($step, $w-$xx), [math]::Min($step, $h-$yy))
        }
    }
    $a.Dispose(); $b.Dispose()
}

$fTitle = New-Object System.Drawing.Font("Segoe UI", 17, [System.Drawing.FontStyle]::Bold)
$fSub   = New-Object System.Drawing.Font("Segoe UI", 9)
$fLab   = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$fSmall = New-Object System.Drawing.Font("Segoe UI", 8)
$white  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,235,238,245))
$grey   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,150,156,170))
$red    = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,255,110,110))
$penG   = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,70,74,86), 1)
$fmt    = New-Object System.Drawing.StringFormat
$fmt.Trimming    = [System.Drawing.StringTrimming]::EllipsisCharacter
$fmt.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap

# ---------------------------------------------------------------- sheet 1: the baseline

$cell = 192; $labelH = 34; $pad = 8; $cols = 6; $headerH = 78
$n = $entries.Count
$nrows = [math]::Ceiling($n / $cols)
$W = $cols * ($cell + $pad) + $pad
$H = $headerH + $nrows * ($cell + $labelH + $pad) + $pad

$r = New-Canvas $W $H; $bmp = $r[0]; $g = $r[1]

$g.DrawString("Pyre - visual baseline: one frame from every picture-making technique", $fTitle, $white, 12, 8)
$g.DrawString("Unity 6000.3.10f1, batch mode, edit mode, no Play. canvas 96x96, frameCount 16, seed 1234, stock defaults, nothing authored.", $fSub, $grey, 14, 38)
$g.DrawString("Frames 2/4/6/7/9/11/13 were rendered for each; the one with the most visible pixels is shown (index in the caption). 2x nearest-neighbour on a checker so transparency reads.", $fSub, $grey, 14, 54)

for ($i = 0; $i -lt $n; $i++) {
    $e  = $entries[$i]
    $cx = $pad + ($i % $cols) * ($cell + $pad)
    $cy = $headerH + [math]::Floor($i / $cols) * ($cell + $labelH + $pad)

    Draw-Checker $g $cx $cy $cell $cell 12

    $path = Join-Path $frames $e.File
    if (Test-Path $path) {
        $img = [System.Drawing.Image]::FromFile($path)
        $g.DrawImage($img, $cx, $cy, $cell, $cell)
        $img.Dispose()
    }
    $g.DrawRectangle($penG, $cx, $cy, $cell, $cell)

    $col = $groupColor[$e.Group]; if (-not $col) { $col = [System.Drawing.Color]::White }
    $gb  = New-Object System.Drawing.SolidBrush($col)
    $rect1 = New-Object System.Drawing.RectangleF(($cx + 2), ($cy + $cell + 2), ($cell - 4), 15)
    $g.DrawString(("{0}  {1}" -f $e.Id, $e.Label), $fLab, $gb, $rect1, $fmt)

    $sub = "{0} | frame {1} | {2}% cover" -f $e.Group, $e.Frame, $e.Cov
    $bad = $false
    if ($e.Note -match 'PIXEL-IDENTICAL') { $sub = "DRAWS A PLAIN DISC - retired slot"; $bad = $true }
    if ($e.Cov -eq '0.00')                { $sub = "RENDERS NOTHING - no bake path exists"; $bad = $true }
    $brush = if ($bad) { $red } else { $grey }
    $rect2 = New-Object System.Drawing.RectangleF(($cx + 2), ($cy + $cell + 18), ($cell - 4), 14)
    $g.DrawString($sub, $fSmall, $brush, $rect2, $fmt)
    $gb.Dispose()
}

$out = Join-Path $root "PYRE_BASELINE_SHEET.png"
$g.Dispose(); $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"wrote $out"

# ---------------------------------------------------------------- sheet 2: filmstrips

$probe = @(2,4,6,7,9,11,13)
$fc = 96; $fpad = 4; $nameW = 152; $rowH = $fc
$W2 = $nameW + $probe.Count * ($fc + $fpad) + $fpad
$H2 = 74 + $n * ($rowH + $fpad) + $fpad

$r2 = New-Canvas $W2 $H2; $bmp2 = $r2[0]; $g2 = $r2[1]
$g2.DrawString("Pyre - the same techniques over time (frames 2, 4, 6, 7, 9, 11, 13 of 16)", $fTitle, $white, 12, 8)
$g2.DrawString("Same specs as the baseline sheet, shown 1:1. Where a strip goes empty, that technique has finished its life by that frame.", $fSub, $grey, 14, 36)

for ($i = 0; $i -lt $n; $i++) {
    $e  = $entries[$i]
    $cy = 74 + $i * ($rowH + $fpad)
    $col = $groupColor[$e.Group]; if (-not $col) { $col = [System.Drawing.Color]::White }
    $gb  = New-Object System.Drawing.SolidBrush($col)
    $rl  = New-Object System.Drawing.RectangleF(6, ($cy + 24), ($nameW - 10), 16)
    $g2.DrawString(("{0} {1}" -f $e.Id, $e.Label), $fLab, $gb, $rl, $fmt)
    $rl2 = New-Object System.Drawing.RectangleF(6, ($cy + 42), ($nameW - 10), 14)
    $g2.DrawString($e.Group, $fSmall, $grey, $rl2, $fmt)
    $gb.Dispose()

    for ($k = 0; $k -lt $probe.Count; $k++) {
        $fx = $nameW + $k * ($fc + $fpad)
        Draw-Checker $g2 $fx $cy $fc $fc 8
        $p = Join-Path $strips ("{0}_f{1:00}.png" -f $e.Id, $probe[$k])
        if (Test-Path $p) {
            $img = [System.Drawing.Image]::FromFile($p)
            $g2.DrawImage($img, $fx, $cy, $fc, $fc)
            $img.Dispose()
        }
        $g2.DrawRectangle($penG, $fx, $cy, $fc, $fc)
        if ($i -eq 0) { $g2.DrawString(("f" + $probe[$k]), $fSmall, $grey, $fx + 2, $cy - 14) }
    }
}

$out2 = Join-Path $root "PYRE_BASELINE_FILMSTRIPS.png"
$g2.Dispose(); $bmp2.Save($out2, [System.Drawing.Imaging.ImageFormat]::Png); $bmp2.Dispose()
"wrote $out2"
