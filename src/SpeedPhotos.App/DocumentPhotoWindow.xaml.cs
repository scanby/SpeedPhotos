using Microsoft.Win32;
using System;
using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpeedPhotos.App
{
    public partial class DocumentPhotoWindow : Window
    {
        private const double Dpi = 96.0;
        private BitmapSource _photo;
        private double _paperWidthCm = 21.59;
        private double _paperHeightCm = 27.94;

        public DocumentPhotoWindow()
        {
            InitializeComponent();
            RenderSheet();
        }

        private void ImportPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Seleccionar fotografía para documento",
                Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Todos los archivos|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.EndInit();
                bitmap.Freeze();
                _photo = bitmap;
                PhotoPreview.Source = _photo;
                PhotoInfoText.Text = System.IO.Path.GetFileName(dialog.FileName) + Environment.NewLine +
                    bitmap.PixelWidth + " × " + bitmap.PixelHeight + " píxeles";
                StatusText.Text = "Fotografía cargada. Revisa el encuadre y la distribución.";
                RenderSheet();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la fotografía.\n\n" + ex.Message,
                    "SpeedPhotos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PaperCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            int index = PaperCombo.SelectedIndex;
            if (index == 1) { _paperWidthCm = 21.0; _paperHeightCm = 29.7; }
            else if (index == 2) { _paperWidthCm = 21.59; _paperHeightCm = 33.02; }
            else { _paperWidthCm = 21.59; _paperHeightCm = 27.94; }
            RenderSheet();
        }

        private void GapText_Changed(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) RenderSheet();
        }

        private double GetGap()
        {
            double value;
            if (!Double.TryParse(GapText.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                Double.TryParse(GapText.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            return Math.Max(0, Math.Min(2, value));
        }

        private void RenderSheet()
        {
            if (SheetCanvas == null) return;
            SheetCanvas.Children.Clear();
            double width = _paperWidthCm * Dpi / 2.54;
            double height = _paperHeightCm * Dpi / 2.54;
            SheetCanvas.Width = width;
            SheetCanvas.Height = height;
            double gap = GetGap() * Dpi / 2.54;
            double photoW = 3.0 * Dpi / 2.54;
            double photoH = 4.0 * Dpi / 2.54;
            int columns = Math.Max(0, (int)Math.Floor((width + gap) / (photoW + gap)));
            int rows = Math.Max(0, (int)Math.Floor((height + gap) / (photoH + gap)));
            double usedW = columns * photoW + Math.Max(0, columns - 1) * gap;
            double usedH = rows * photoH + Math.Max(0, rows - 1) * gap;
            double startX = (width - usedW) / 2.0;
            double startY = (height - usedH) / 2.0;

            if (_photo != null)
            {
                for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    var image = new Image { Source = _photo, Width = photoW, Height = photoH, Stretch = Stretch.UniformToFill };
                    Canvas.SetLeft(image, startX + col * (photoW + gap));
                    Canvas.SetTop(image, startY + row * (photoH + gap));
                    SheetCanvas.Children.Add(image);
                }
            }

            LayoutInfoText.Text = _photo == null
                ? "Cada fotografía mide 3 × 4 cm. Importa una imagen para ver la hoja."
                : columns + " columnas × " + rows + " filas = " + (columns * rows) +
                  " fotografías de 3 × 4 cm. La distribución se centra en la hoja.";
        }

        private DrawingVisual CreatePrintedSheet(double pageWidthDip, double pageHeightDip)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidthDip, pageHeightDip));
                if (_photo == null) return visual;

                double gap = GetGap() * Dpi / 2.54;
                double photoW = 3.0 * Dpi / 2.54;
                double photoH = 4.0 * Dpi / 2.54;
                int columns = Math.Max(0, (int)Math.Floor((pageWidthDip + gap) / (photoW + gap)));
                int rows = Math.Max(0, (int)Math.Floor((pageHeightDip + gap) / (photoH + gap)));
                double usedW = columns * photoW + Math.Max(0, columns - 1) * gap;
                double usedH = rows * photoH + Math.Max(0, rows - 1) * gap;
                double startX = (pageWidthDip - usedW) / 2.0;
                double startY = (pageHeightDip - usedH) / 2.0;
                double sourceRatio = (double)_photo.PixelWidth / _photo.PixelHeight;
                double targetRatio = 3.0 / 4.0;
                double sourceX = 0, sourceY = 0, sourceW = _photo.PixelWidth, sourceH = _photo.PixelHeight;
                if (sourceRatio > targetRatio)
                {
                    sourceW = _photo.PixelHeight * targetRatio;
                    sourceX = (_photo.PixelWidth - sourceW) / 2.0;
                }
                else
                {
                    sourceH = _photo.PixelWidth / targetRatio;
                    sourceY = (_photo.PixelHeight - sourceH) / 2.0;
                }
                var cropped = new CroppedBitmap(_photo, new Int32Rect(
                    Math.Max(0, (int)Math.Round(sourceX)),
                    Math.Max(0, (int)Math.Round(sourceY)),
                    Math.Max(1, Math.Min(_photo.PixelWidth - (int)Math.Round(sourceX), (int)Math.Round(sourceW))),
                    Math.Max(1, Math.Min(_photo.PixelHeight - (int)Math.Round(sourceY), (int)Math.Round(sourceH)))));
                cropped.Freeze();
                for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    var rect = new Rect(startX + col * (photoW + gap), startY + row * (photoH + gap), photoW, photoH);
                    dc.DrawImage(cropped, rect);
                }
            }
            return visual;
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_photo == null)
            {
                MessageBox.Show(this, "Primero importa una fotografía.", "SpeedPhotos",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true) return;
                double printableWidth = dialog.PrintableAreaWidth;
                double printableHeight = dialog.PrintableAreaHeight;
                if (printableWidth <= 0 || printableHeight <= 0)
                {
                    MessageBox.Show(this, "La impresora no informó un área imprimible válida.",
                        "SpeedPhotos", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DrawingVisual page = CreatePrintedSheet(printableWidth, printableHeight);
                dialog.PrintVisual(page, "SpeedPhotos - Fotografías 3 x 4 cm");
                StatusText.Text = "Trabajo enviado a la impresora. Verifica que el papel seleccionado coincida con la hoja.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo imprimir la hoja.\n\n" + ex.Message,
                    "SpeedPhotos - Impresión", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}