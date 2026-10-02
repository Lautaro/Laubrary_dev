param([string]$In,[string]$Out,[int]$X,[int]$Y,[int]$W,[int]$H)
Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Bitmap]::FromFile($In)
$W=[Math]::Min($W,$src.Width-$X); $H=[Math]::Min($H,$src.Height-$Y)
$dst=New-Object System.Drawing.Bitmap $W,$H
$g=[System.Drawing.Graphics]::FromImage($dst)
$g.DrawImage($src,(New-Object System.Drawing.Rectangle 0,0,$W,$H),(New-Object System.Drawing.Rectangle $X,$Y,$W,$H),[System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $dst.Save($Out,[System.Drawing.Imaging.ImageFormat]::Png); $dst.Dispose(); $src.Dispose()
Write-Output "cropped $Out ${W}x${H}"
