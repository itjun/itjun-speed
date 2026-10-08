Add-Type -AssemblyName System.Drawing
$dir = 'C:\Users\itjun\Documents\iLearn\itjun-speed\src\LanSpeed.App\Assets'
$images = @()
foreach ($size in @(16, 32, 48)) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 122, 204))
  $g.FillEllipse($brush, 0, 0, $size, $size)
  $font = New-Object System.Drawing.Font('Microsoft YaHei UI', ($size * 0.42), [System.Drawing.FontStyle]::Bold)
  $fmt = New-Object System.Drawing.StringFormat
  $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
  $g.DrawString([string][char]0x901F, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 0, $size, $size)), $fmt)
  $g.Dispose()
  $png = New-Object System.IO.MemoryStream
  $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $images += [pscustomobject]@{ Size = $size; Data = $png.ToArray() }
}
$fs = [System.IO.File]::Create("$dir\app.ico")
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
  $w.Write([byte]$img.Size); $w.Write([byte]$img.Size); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint32]$img.Data.Length); $w.Write([uint32]$offset)
  $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write($img.Data) }
$w.Flush(); $fs.Close()
Write-Host ("app.ico: " + (Get-Item "$dir\app.ico").Length + " bytes")
