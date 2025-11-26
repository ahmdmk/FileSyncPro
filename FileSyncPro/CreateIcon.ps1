# PowerShell script to create a simple application icon
# This creates a basic icon with sync/folder theme

Add-Type -AssemblyName System.Drawing

# Create a 256x256 bitmap for high quality
$bitmap = New-Object System.Drawing.Bitmap(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Fill background with transparent
$graphics.Clear([System.Drawing.Color]::Transparent)

# Define Petrofac red color
$petrofacRed = [System.Drawing.Color]::FromArgb(227, 24, 55)
$blueColor = [System.Drawing.Color]::FromArgb(0, 120, 215)
$whiteColor = [System.Drawing.Color]::White

# Draw outer circle (Petrofac red)
$pen = New-Object System.Drawing.Pen($petrofacRed, 20)
$graphics.DrawEllipse($pen, 30, 30, 196, 196)

# Draw folder shape in blue
$folderBrush = New-Object System.Drawing.SolidBrush($blueColor)
$folderRect = New-Object System.Drawing.Rectangle(80, 100, 96, 80)
$graphics.FillRectangle($folderBrush, $folderRect)

# Draw folder tab
$tabRect = New-Object System.Drawing.Rectangle(80, 85, 50, 15)
$graphics.FillRectangle($folderBrush, $tabRect)

# Draw sync arrows (white)
$arrowPen = New-Object System.Drawing.Pen($whiteColor, 8)
$arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor

# Up arrow
$graphics.DrawLine($arrowPen, 115, 155, 115, 125)

# Down arrow
$graphics.DrawLine($arrowPen, 145, 125, 145, 155)

# Save as PNG first
$pngPath = Join-Path $PSScriptRoot "app_temp.png"
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "PNG created at: $pngPath"
Write-Host ""
Write-Host "To convert to ICO format, you can:"
Write-Host "1. Use an online converter like https://convertio.co/png-ico/"
Write-Host "2. Use ImageMagick: magick convert app_temp.png -define icon:auto-resize=256,128,64,48,32,16 app.ico"
Write-Host "3. Use an icon editor software"
Write-Host ""
Write-Host "Please convert the PNG to ICO format and save as 'app.ico' in the project root."

# Cleanup
$graphics.Dispose()
$bitmap.Dispose()
$pen.Dispose()
$folderBrush.Dispose()
$arrowPen.Dispose()
