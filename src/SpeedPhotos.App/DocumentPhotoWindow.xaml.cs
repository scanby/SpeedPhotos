using Microsoft.Win32;
using System;
using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpeedPhotos.App
{
    public partial class DocumentPhotoWindow : Window
    {
        private const double Dpi = 96.0;
        private const double PreviewWidth = 225.0;
        private const double PreviewHeight = 300.0;
        private BitmapSource _photo;
        private double _paperWidthCm = 21.59;
        private double _paperHeightCm = 27.94;
        private double _previewScale;
        private double _basePreviewScale;
        private double _zoom = 1.0;
        private bool _isResizing;
        private string _resizeCorner;
        private Point _resizeStart;
        private double _resizeStartZoom;
        private double _imageLeft;
        private double _imageTop;
        private Point _dragStart;
        private double _dragImageLeft;
        private double _dragImageTop;
        private bool _isDragging;

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
                CenterImageInFrame();
                PhotoInfoText.Text = System.IO.Path.GetFileName(dialog.FileName) + Environment.NewLine +
                    bitmap.PixelWidth + " × " + bitmap.PixelHeight + " píxeles";
                CenterCropButton.IsEnabled = true;
                StatusText.Text = "Fotografía cargada. Arrastra la imagen para ajustar el encuadre.";
                RenderSheet();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la fotografía.\n\n" + ex.Message,
                    "SpeedPhotos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CenterImageInFrame()
        {
            if (_photo == null) return;
            _basePreviewScale = Math.Max(PreviewWidth / _photo.PixelWidth, PreviewHeight / _photo.PixelHeight);
            _zoom = 1.0;
            PhotoPreview.Source = _photo;
            _previewScale = _basePreviewScale;
            PhotoPreview.Width = _photo.PixelWidth * _previewScale;
            PhotoPreview.Height = _photo.PixelHeight * _previewScale;
            _imageLeft = (PreviewWidth - PhotoPreview.Width) / 2.0;
            _imageTop = (PreviewHeight - PhotoPreview.Height) / 2.0;
            ApplyImagePosition();
        }

        private void ApplyImagePosition()
        {
            if (_photo == null) return;
            _imageLeft = Math.Min(0, Math.Max(PreviewWidth - PhotoPreview.Width, _imageLeft));
            _imageTop = Math.Min(0, Math.Max(PreviewHeight - PhotoPreview.Height, _imageTop));
            Canvas.SetLeft(PhotoPreview, _imageLeft);
            Canvas.SetTop(PhotoPreview, _imageTop);
        }

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_photo == null) return;
            var handle = sender as FrameworkElement;
            if (handle == null) return;
            _isResizing = true;
            _resizeCorner = Convert.ToString(handle.Tag, CultureInfo.InvariantCulture);
            _resizeStart = e.GetPosition(PhotoCropCanvas);
            _resizeStartZoom = _zoom;
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizing || _photo == null) return;
            Point current = e.GetPosition(PhotoCropCanvas);
            double dx = current.X - _resizeStart.X;
            double dy = current.Y - _resizeStart.Y;
            double directionX = _resizeCorner.Contains("Left") ? -1.0 : 1.0;
            double directionY = _resizeCorner.Contains("Top") ? -1.0 : 1.0;
            double outwardDistance = (dx * directionX + dy * directionY) / 2.0;
            double nextZoom = _resizeStartZoom * Math.Exp(outwardDistance / 220.0);
            _zoom = Math.Max(1.0, Math.Min(5.0, nextZoom));

            double oldCenterX = _imageLeft + PhotoPreview.Width / 2.0;
            double oldCenterY = _imageTop + PhotoPreview.Height / 2.0;
            _previewScale = _basePreviewScale * _zoom;
            PhotoPreview.Width = _photo.PixelWidth * _previewScale;
            PhotoPreview.Height = _photo.PixelHeight * _previewScale;
            _imageLeft = oldCenterX - PhotoPreview.Width / 2.0;
            _imageTop = oldCenterY - PhotoPreview.Height / 2.0;
            ApplyImagePosition();
        }

        private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizing) return;
            _isResizing = false;
            var handle = sender as UIElement;
            if (handle != null) handle.ReleaseMouseCapture();
            RenderSheet();
            StatusText.Text = "Ampliación y encuadre actualizados. La hoja de impresión usa el mismo recorte.";
            e.Handled = true;
        }

        private void PhotoPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_photo == null) return;
            _isDragging = true;
            _dragStart = e.GetPosition(PhotoCropCanvas);
            _dragImageLeft = _imageLeft;
            _dragImageTop = _imageTop;
            PhotoPreview.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void PhotoPreview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _photo == null) return;
            Point current = e.GetPosition(PhotoCropCanvas);
            _imageLeft = _dragImageLeft + current.X - _dragStart.X;
            _imageTop = _dragImageTop + current.Y - _dragStart.Y;
            ApplyImagePosition();
        }

        private void PhotoPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            PhotoPreview.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
            RenderSheet();
            StatusText.Text = "Encuadre actualizado. La hoja de impresión usa este mismo recorte.";
        }

        private void CenterCrop_Click(object sender, RoutedEventArgs e)
        {
            CenterImageInFrame();
            RenderSheet();
            StatusText.Text = "Encuadre centrado nuevamente.";
        }

        private Int32Rect GetCropRectangle()
        {
            double sourceX = -_imageLeft / _previewScale;
            double sourceY = -_imageTop / _previewScale;
            double sourceWidth = PreviewWidth / _previewScale;
            double sourceHeight = PreviewHeight / _previewScale;

            int x = Math.Max(0, (int)Math.Floor(sourceX));
            int y = Math.Max(0, (int)Math.Floor(sourceY));
            int right = Math.Min(_photo.PixelWidth, (int)Math.Ceiling(sourceX + sourceWidth));
            int bottom = Math.Min(_photo.PixelHeight, (int)Math.Ceiling(sourceY + sourceHeight));
            return new Int32Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
        }

        private BitmapSource GetFramedPhoto()
        {
            var cropped = new CroppedBitmap(_photo, GetCropRectangle());
            cropped.Freeze();
            return cropped;
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
                BitmapSource framedPhoto = GetFramedPhoto();
                for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    var image = new Image { Source = framedPhoto, Width = photoW, Height = photoH, Stretch = Stretch.Fill };
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
                BitmapSource framedPhoto = GetFramedPhoto();

                for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    var rect = new Rect(startX + col * (photoW + gap), startY + row * (photoH + gap), photoW, photoH);
                    dc.DrawImage(framedPhoto, rect);
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