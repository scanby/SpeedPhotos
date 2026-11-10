# SpeedPhotos

Aplicación de escritorio para Windows orientada a organizar fotografías, crear composiciones listas para impresión y preparar fotografías tipo carnet.

## Objetivo

Facilitar la impresión de varias fotografías en una misma hoja, aprovechando el papel y manteniendo las dimensiones físicas seleccionadas.

## Compatibilidad objetivo

- Windows 7 SP1 como versión mínima prevista.
- Windows 8, 10 y 11.
- Funcionamiento local; las funciones principales no dependerán de internet.

> **Nota de compatibilidad:** Windows 7 está fuera de soporte. La compatibilidad real dependerá de las actualizaciones del sistema, la versión de .NET Framework y los controladores de impresora. Se validará en equipos reales antes de publicar instaladores.

## Stack inicial

- C# 7.3
- WPF
- .NET Framework 4.8
- SQLite para datos locales

## Funciones previstas

### MVP — versión 0.1
- Importación de múltiples imágenes.
- Galería de miniaturas.
- Selección de tamaño y orientación del papel.
- Distribución automática por cantidad de fotos o dimensiones en centímetros.
- Ajuste de márgenes y separación.
- Vista previa de impresión.
- Selección de impresora.
- Exportación a JPG y PDF.

### Versión 0.2
- Plantillas de collage.
- Fotografías tipo carnet (inicialmente 3 × 4 cm).
- Duplicación de fotografías.
- Líneas de corte.
- Integración con el menú contextual del Explorador de Windows.

## Estructura prevista

```text
SpeedPhotos/
├── src/
│   ├── SpeedPhotos.App/
│   ├── SpeedPhotos.Core/
│   ├── SpeedPhotos.LayoutEngine/
│   ├── SpeedPhotos.Imaging/
│   ├── SpeedPhotos.Printing/
│   ├── SpeedPhotos.Export/
│   └── SpeedPhotos.Infrastructure/
├── tests/
├── docs/
└── installer/
```

## Principios de diseño

1. Las medidas físicas se expresan en milímetros o centímetros y se convierten a unidades de impresión de forma consistente.
2. El área imprimible real de la impresora debe respetarse.
3. La distribución automática propone alternativas, pero el usuario conserva el control manual.
4. La previsualización debe representar fielmente el resultado exportado o impreso.
5. Las funciones esenciales deben operar sin conexión.

## Estado

Repositorio inicializado con documentación. El siguiente hito es crear la solución de Visual Studio y el esqueleto de los proyectos.

Consulta [la arquitectura](docs/ARCHITECTURE.md) y el [plan de desarrollo](docs/ROADMAP.md).


## Integración con el Explorador de Windows

Se incluye un registro opcional del menú contextual para abrir imágenes directamente con SpeedPhotos. El registro es por usuario y no requiere permisos de administrador.

Después de compilar la aplicación, abre PowerShell en la carpeta del repositorio y ejecuta:

```powershell
.\installer\Register-ContextMenu.ps1 -ExePath ".\src\SpeedPhotos.App\bin\Debug\SpeedPhotos.exe"
```

Luego selecciona una o varias imágenes en el Explorador, haz clic derecho y elige **Abrir con SpeedPhotos**. En Windows 11, la opción puede estar dentro de **Mostrar más opciones**.

Para quitar la opción del menú contextual:

```powershell
.\installer\Unregister-ContextMenu.ps1
```

Si PowerShell bloquea la ejecución del script, ejecuta el comando con `-ExecutionPolicy Bypass -File` y la ruta del script. La opción se registra para el usuario actual de Windows.
