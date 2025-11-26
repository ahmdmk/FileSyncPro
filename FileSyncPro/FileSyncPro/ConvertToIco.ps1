# Convert PNG to ICO using .NET
Add-Type -AssemblyName System.Drawing

$pngPath = Join-Path $PSScriptRoot "app_temp.png"
$icoPath = Join-Path $PSScriptRoot "app.ico"

try {
    # Load the PNG
    $png = [System.Drawing.Image]::FromFile($pngPath)

    # Create icon from image
    $icon = [System.Drawing.Icon]::FromHandle(([System.Drawing.Bitmap]$png).GetHicon())

    # Save as ICO
    $fileStream = [System.IO.FileStream]::new($icoPath, [System.IO.FileMode]::Create)
    $icon.Save($fileStream)
    $fileStream.Close()

    Write-Host "ICO file created successfully at: $icoPath" -ForegroundColor Green

    # Cleanup
    $icon.Dispose()
    $png.Dispose()
}
catch {
    Write-Host "Error creating ICO: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Alternative: Please use an online converter or ImageMagick to convert app_temp.png to app.ico"
}
