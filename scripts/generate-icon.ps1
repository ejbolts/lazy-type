# Reproduce Native.MakeIcon's existing microphone design for Windows shell icons.
# Run this on Windows when changing the icon, and commit the generated .ico.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\LazyType\Assets\LazyType.ico'
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(65, 98, 211))
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, 2.5)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.ScaleTransform($size / 32.0, $size / 32.0)
        $graphics.FillEllipse($brush, 1, 1, 30, 30)
        $graphics.DrawLine($pen, 16, 8, 16, 17)
        $graphics.DrawArc($pen, 10, 10, 12, 13, 0, 180)
        $graphics.DrawLine($pen, 16, 23, 16, 27)
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        ,$stream.ToArray()
    } finally {
        $stream.Dispose(); $pen.Dispose(); $brush.Dispose()
        $graphics.Dispose(); $bitmap.Dispose()
    }
}
$file = [IO.File]::Create($destination)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally {
    $writer.Dispose(); $file.Dispose()
}
Write-Output "Generated: $destination"
