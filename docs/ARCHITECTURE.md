# Arquitectura inicial

## Plataforma

La aplicación se desarrollará inicialmente con WPF, C# 7.3 y .NET Framework 4.8. Esta elección prioriza la compatibilidad heredada solicitada. La ejecución en Windows 7 SP1 debe comprobarse con los prerrequisitos y actualizaciones adecuados de .NET Framework 4.8.

## Capas y responsabilidades

| Proyecto | Responsabilidad |
|---|---|
| SpeedPhotos.App | Ventanas WPF, navegación, comandos y ViewModels |
| SpeedPhotos.Core | Modelos de dominio, reglas y contratos |
| SpeedPhotos.LayoutEngine | Cálculo geométrico y algoritmos de distribución |
| SpeedPhotos.Imaging | Carga, orientación, recorte y resolución de imágenes |
| SpeedPhotos.Printing | Impresoras, papel, área imprimible y envío a impresión |
| SpeedPhotos.Export | Renderizado y exportación a JPG/PDF |
| SpeedPhotos.Infrastructure | Persistencia SQLite e integración con Windows |

## Reglas importantes

- Mantener la lógica de distribución fuera de la interfaz gráfica.
- Trabajar internamente con medidas físicas explícitas; evitar depender de píxeles de pantalla.
- Considerar DPI, resolución de salida y área imprimible del controlador.
- No modificar los archivos originales al recortar o ajustar fotografías.
- Ejecutar las operaciones pesadas de imagen fuera del hilo de interfaz.
- Aislar las dependencias de PDF e imagen y verificar que soporten el sistema operativo objetivo.

## Flujo principal

1. El usuario importa imágenes.
2. Imaging obtiene dimensiones, orientación y metadatos relevantes.
3. El usuario configura papel, márgenes, separación y tamaño de fotografía.
4. LayoutEngine calcula distribuciones candidatas.
5. La interfaz presenta una previsualización editable.
6. Printing o Export renderiza el diseño final con las dimensiones físicas correctas.

## Riesgo de compatibilidad

Windows 7 está fuera de soporte. Se debe probar en una instalación limpia de Windows 7 SP1 y verificar controladores, fuentes, codecs y dependencias externas. No se debe asumir que una biblioteca moderna sigue soportando Windows 7.
