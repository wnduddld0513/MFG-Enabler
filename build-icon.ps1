# Convert the project PNG to a Windows ICO containing multiple PNG-encoded resolutions.
# No NVIDIA reference files are needed to rebuild the project icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot 'WinUI/Assets'
$inputPath = Join-Path $assetDirectory 'MFG-Enabler.png'
$outputPath = Join-Path $assetDirectory 'MFG-Enabler.ico'
$sourceImage = [System.Drawing.Image]::FromFile($inputPath)
try {
    $sizes = @(16,20,24,32,40,48,64,128,256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size,$size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = New-Object System.IO.MemoryStream
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($sourceImage,0,0,$size,$size)
            $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,$stream.ToArray()
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $output = [System.IO.File]::Create($outputPath)
    $writer = New-Object System.IO.BinaryWriter($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }
} finally { $sourceImage.Dispose() }
Write-Host "Created $outputPath"
