param(
    [string]$Out = "$PSScriptRoot\app.ico"
)

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [double]$size * 0.04
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2*$pad), ($size - 2*$pad))
    $radius = [double]$size * 0.22

    # rounded rect path
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 32, 52, 74)
    $c2 = [System.Drawing.Color]::FromArgb(255, 12, 17, 24)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # cyan accent ring
    if ($size -ge 24) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 79, 195, 247), ([double]$size * 0.035))
        $g.DrawPath($pen, $path)
        $pen.Dispose()
    }

    # waveform bars (left/right) + play triangle (center)
    $accent = [System.Drawing.Color]::FromArgb(255, 79, 195, 247)
    $bars = @(0.50, 0.82, 1.00, 0.82, 0.50)
    $barW = [double]$size * 0.085
    $barGap = [double]$size * 0.052
    $totalW = $bars.Count * $barW + ($bars.Count - 1) * $barGap
    $startX = ($size - $totalW) / 2
    $cy = $size / 2.0
    $maxH = [double]$size * 0.50

    $wbrush = New-Object System.Drawing.SolidBrush($accent)
    for ($i = 0; $i -lt $bars.Count; $i++) {
        $h = $maxH * $bars[$i]
        $x = $startX + $i * ($barW + $barGap)
        $r = New-Object System.Drawing.RectangleF($x, ($cy - $h/2), $barW, $h)
        $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $rr = [math]::Min($barW, $h) / 2
        $dd = $rr * 2
        if ($dd -lt $h) {
            $bp.AddArc($r.X, $r.Y, $dd, $dd, 180, 180)
            $bp.AddArc(($r.Right - $dd), $r.Y, $dd, $dd, 270, 180)
        } else {
            $bp.AddRectangle($r)
        }
        $bp.CloseFigure()
        $g.FillPath($wbrush, $bp)
        $bp.Dispose()
    }
    $wbrush.Dispose()

    $g.Dispose()
    return $bmp
}

function Convert-BitmapToIcoBytes($bmp) {
    $w = $bmp.Width
    $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$w)
    $bw.Write([Int32]($h * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($w * $h * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buffer = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
        # rows bottom-up
        for ($y = $h - 1; $y -ge 0; $y--) {
            $bw.Write($buffer, $y * $stride, $w * 4)
        }
    } finally {
        $bmp.UnlockBits($data)
    }

    # AND mask: all zeros (alpha channel handles transparency), padded to 32-bit rows
    $maskRow = [math]::Floor(($w + 31) / 32) * 4
    $zeros = New-Object byte[] ($maskRow * $h)
    $bw.Write($zeros, 0, $zeros.Length)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return $bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $ms.Dispose()
    } else {
        $bytes = Convert-BitmapToIcoBytes $bmp
    }
    $images += ,@($s, $bytes)
    $bmp.Dispose()
}

$outMs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($outMs)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type = icon
$bw.Write([UInt16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]
    $bytes = $img[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($img in $images) {
    $bw.Write($img[1], 0, $img[1].Length)
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($Out, $outMs.ToArray())
$bw.Dispose(); $outMs.Dispose()

Write-Host "icon written: $Out ($([math]::Round((Get-Item $Out).Length/1KB,1)) KB)"
