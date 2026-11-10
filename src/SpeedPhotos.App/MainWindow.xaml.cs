using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpeedPhotos.App
{
    public partial class MainWindow : Window
    {
        private const double Dpi = 96.0;
        private readonly ObservableCollection<PhotoItem> _photos = new ObservableCollection<PhotoItem>();
        private int _photosPerPage;
        private int _pageCount;
        private int _currentPage;
        private double _pageWidthCm;
        private double _pageHeightCm;
        private double _photoWidthCm;
        private double _photoHeightCm;
        private double _marginCm;
        private double _gapCm;
        private int _columns;
        private int _rows;

        public MainWindow()
        {
            InitializeComponent();
            PhotosList.ItemsSource = _photos;
            DrawEmptyPage();
        }

        private void ImportPhotos_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Seleccionar fotografías",
                Filter = "Archivos de imagen|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Todos los archivos|*.*",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            int added = 0;
            foreach (string file in dialog.FileNames)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(file);
                    bitmap.DecodePixelWidth = 180;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _photos.Add(new PhotoItem { FileName = System.IO.Path.GetFileName(file), FilePath = file, Thumbnail = bitmap });
                    added++;
                }
                catch { }
            }
            StatusText.Text = added + " fotografía(s) importada(s). Total: " + _photos.Count + ". Genera la distribución para actualizar las hojas.";
        }

        private void ClearPhotos_Click(object sender, RoutedEventArgs e)
        {
            _photos.Clear();
            _photosPerPage = 0;
            _pageCount = 0;
            _currentPage = 0;
            DrawEmptyPage();
            UpdatePageNavigation();
            StatusText.Text = "Selección de fotografías vaciada.";
        }

        private void Settings_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && _photos.Count > 0) GenerateLayout_Click(this, new RoutedEventArgs());
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage <= 0) return;
            _currentPage--;
            RenderCurrentPage();
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage >= _pageCount - 1) return;
            _currentPage++;
            RenderCurrentPage();
        }

        private void GenerateLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_photos.Count == 0)
            {
                StatusText.Text = "Primero importa al menos una fotografía.";
                return;
            }

            double photoWidth, photoHeight, margin, gap;
            if (!ReadPositive(PhotoWidthText.Text, out photoWidth) ||
                !ReadPositive(PhotoHeightText.Text, out photoHeight) ||
                !ReadNonNegative(MarginText.Text, out margin) ||
                !ReadNonNegative(GapText.Text, out gap))
            {
                MessageBox.Show("Verifica las dimensiones, el margen y la separación. Usa números válidos en centímetros.", "Configuración no válida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double pageWidth, pageHeight;
            GetPaperSize(out pageWidth, out pageHeight);
            double usableWidth = pageWidth - margin * 2;
            double usableHeight = pageHeight - margin * 2;
            if (usableWidth <= 0 || usableHeight <= 0 || photoWidth > usableWidth || photoHeight > usableHeight)
            {
                MessageBox.Show("Las fotografías o los márgenes son mayores que el área imprimible de la hoja.", "No caben en la hoja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Cada fotografía conserva exactamente el ancho y alto configurados.
            int maxColumns = (int)Math.Floor((usableWidth + gap) / (photoWidth + gap));
            int maxRows = (int)Math.Floor((usableHeight + gap) / (photoHeight + gap));
            if (maxColumns < 1 || maxRows < 1)
            {
                MessageBox.Show("No cabe ninguna fotografía con esas dimensiones, márgenes y separación.", "No caben en la hoja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int physicalCapacity = maxColumns * maxRows;
            int requested = CountCombo.SelectedIndex == 0
                ? physicalCapacity
                : int.Parse(((ComboBoxItem)CountCombo.SelectedItem).Content.ToString());
            _photosPerPage = Math.Min(physicalCapacity, requested);
            if (_photosPerPage < 1) _photosPerPage = 1;

            _columns = Math.Min(maxColumns, _photosPerPage);
            _rows = (int)Math.Ceiling((double)_photosPerPage / _columns);
            _pageWidthCm = pageWidth;
            _pageHeightCm = pageHeight;
            _photoWidthCm = photoWidth;
            _photoHeightCm = photoHeight;
            _marginCm = margin;
            _gapCm = gap;

            _pageCount = (int)Math.Ceiling((double)_photos.Count / _photosPerPage);
            _currentPage = 0;
            RenderCurrentPage();

            int lastPagePhotos = _photos.Count % _photosPerPage;
            if (lastPagePhotos == 0) lastPagePhotos = _photosPerPage;
            StatusText.Text = _photos.Count + " fotografía(s) distribuidas en " + _pageCount +
                " hoja(s). " + _photosPerPage + " por hoja; tamaño fijo " +
                photoWidth.ToString("0.##") + " × " + photoHeight.ToString("0.##") + " cm.";
        }

        private void RenderCurrentPage()
        {
            double pageW = _pageWidthCm * Dpi / 2.54;
            double pageH = _pageHeightCm * Dpi / 2.54;
            PreviewCanvas.Width = pageW;
            PreviewCanvas.Height = pageH;
            PreviewCanvas.Children.Clear();
            PreviewCanvas.Background = Brushes.White;

            double photoW = _photoWidthCm * Dpi / 2.54;
            double photoH = _photoHeightCm * Dpi / 2.54;
            double margin = _marginCm * Dpi / 2.54;
            double gap = _gapCm * Dpi / 2.54;
            int startIndex = _currentPage * _photosPerPage;
            int countOnPage = Math.Min(_photosPerPage, _photos.Count - startIndex);
            int actualRows = (int)Math.Ceiling((double)countOnPage / _columns);
            double gridWidth = _columns * photoW + (_columns - 1) * gap;
            double gridHeight = actualRows * photoH + (actualRows - 1) * gap;
            double availableW = pageW - margin * 2;
            double availableH = pageH - margin * 2;
            double startX = margin + Math.Max(0, (availableW - gridWidth) / 2.0);
            double startY = margin + Math.Max(0, (availableH - gridHeight) / 2.0);

            for (int i = 0; i < countOnPage; i++)
            {
                int row = i / _columns;
                int col = i % _columns;
                int photoIndex = startIndex + i;
                double x = startX + col * (photoW + gap);
                double y = startY + row * (photoH + gap);

                var frame = new Border
                {
                    Width = photoW,
                    Height = photoH,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 205)),
                    BorderThickness = new Thickness(0.5),
                    ClipToBounds = true,
                    Background = Brushes.White
                };
                var image = new Image { Stretch = Stretch.UniformToFill };
                var source = new BitmapImage();
                source.BeginInit();
                source.UriSource = new Uri(_photos[photoIndex].FilePath);
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.EndInit();
                source.Freeze();
                image.Source = source;
                frame.Child = image;
                Canvas.SetLeft(frame, x);
                Canvas.SetTop(frame, y);
                PreviewCanvas.Children.Add(frame);
            }

            UpdatePageNavigation();
        }

        private void UpdatePageNavigation()
        {
            PageIndicator.Text = _pageCount == 0 ? "Hoja 0 de 0" : "Hoja " + (_currentPage + 1) + " de " + _pageCount;
            PreviousPageButton.IsEnabled = _currentPage > 0;
            NextPageButton.IsEnabled = _pageCount > 0 && _currentPage < _pageCount - 1;
        }

        private void GetPaperSize(out double width, out double height)
        {
            switch (PaperCombo.SelectedIndex)
            {
                case 1: width = 21.0; height = 29.7; break;
                case 2: width = 21.59; height = 33.0; break;
                default: width = 21.59; height = 27.94; break;
            }
        }

        private static bool ReadPositive(string text, out double value)
        {
            return double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static bool ReadNonNegative(string text, out double value)
        {
            return double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private void DrawEmptyPage()
        {
            PreviewCanvas.Width = 816;
            PreviewCanvas.Height = 1056;
            PreviewCanvas.Children.Clear();
            var hint = new TextBlock { Text = "Importa fotografías y genera una distribución", Foreground = Brushes.Gray, FontSize = 22 };
            PreviewCanvas.Children.Add(hint);
            Canvas.SetLeft(hint, 130);
            Canvas.SetTop(hint, 500);
        }
    }

    public class PhotoItem
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public BitmapImage Thumbnail { get; set; }
    }
}
