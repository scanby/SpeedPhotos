param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = "Stop"
$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not (Test-Path -LiteralPath $resolvedExe -PathType Leaf)) {
    throw "No se encontró SpeedPhotos.exe en: $ExePath"
}

$keyPath = "HKCU:\Software\Classes\SystemFileAssociations\image\shell\OpenWithSpeedPhotos"
New-Item -Path $keyPath -Force | Out-Null
Set-Item -Path $keyPath -Value "Abrir con SpeedPhotos"
New-ItemProperty -Path $keyPath -Name "Icon" -Value $resolvedExe -PropertyType String -Force | Out-Null
New-ItemProperty -Path $keyPath -Name "MultiSelectModel" -Value "Player" -PropertyType String -Force | Out-Null

$commandPath = Join-Path $keyPath "command"
New-Item -Path $commandPath -Force | Out-Null
Set-Item -Path $commandPath -Value ('"' + $resolvedExe + '" %*')

Write-Host "Menú contextual instalado correctamente para el usuario actual."
Write-Host "Ejecutable: $resolvedExe"
Write-Host "En Windows 11 puede aparecer dentro de 'Mostrar más opciones'."
