$ErrorActionPreference = "Stop"
$keyPath = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\OpenWithSpeedPhotos"
if (Test-Path -LiteralPath $keyPath) {
    Remove-Item -LiteralPath $keyPath -Recurse -Force
    Write-Host "Se quitó 'Abrir con SpeedPhotos' del menú contextual."
} else {
    Write-Host "La opción no estaba instalada."
}
