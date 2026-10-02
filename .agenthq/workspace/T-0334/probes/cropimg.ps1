# crop.ps1 -In a.png -Out b.png -Rect "x,y,w,h" [-Scale 1]
param([Parameter(Mandatory=$true)][string]$In,
      [Parameter(Mandatory=$true)][string]$Out,
      [Parameter(Mandatory=$true)][string]$Rect,
      [double]$Scale = 1)
Add-Type -AssemblyName System.Drawing
if (-not (Test-Path $In)) { Write-Output "no input: $In"; exit 1 }
$p = $Rect -split ','
$cx=[int]$p[0]; $cy=[int]$p[1]; $cw=[int]$p[2]; $ch=[int]$p[3]
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $In))
if ($cx -lt 0) { $cx = 0 }; if ($cy -lt 0) { $cy = 0 }
if ($cx + $cw -gt $src.Width)  { $cw = $src.Width  - $cx }
if ($cy + $ch -gt $src.Height) { $ch = $src.Height - $cy }
$dw=[int][math]::Round($cw*$Scale); $dh=[int][math]::Round($ch*$Scale)
$dst = New-Object System.Drawing.Bitmap($dw, $dh)
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0,0,$dw,$dh)), (New-Object System.Drawing.Rectangle($cx,$cy,$cw,$ch)), [System.Drawing.GraphicsUnit]::Pixel)
$dst.Save($Out)
$g.Dispose(); $dst.Dispose(); $src.Dispose()
Write-Output ("wrote {0} {1}x{2}" -f $Out, $dw, $dh)
