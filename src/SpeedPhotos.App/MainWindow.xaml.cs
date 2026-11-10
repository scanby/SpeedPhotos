using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
        private readonly List<PhotoPlacement> _placements = new List<PhotoPlacement>();
        private int _pageCount, _currentPage;
        private double _pageWidthCm, _pageHeightCm;

        public MainWindow()
        {
            InitializeComponent();
            PhotosList.ItemsSource = _photos;
            DrawEmptyPage();
        }

        private void ImportPhotos_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Seleccionar fotografías", Filter = "Archivos de imagen|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Todos los archivos|*.*", Multiselect = true };
            if (dialog.ShowDialog() != true) return;
            int added = 0;
            foreach (string file in dialog.FileNames)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.UriSource = new Uri(file); bitmap.DecodePixelWidth = 180;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                    _photos.Add(new PhotoItem { FileName = System.IO.Path.GetFileName(file), FilePath = file, Thumbnail = bitmap,
                        PixelWidth = bitmap.PixelWidth, PixelHeight = bitmap.PixelHeight });
                    added++;
                }
                catch { }
            }
            StatusText.Text = added + " fotografía(s) importada(s). Total: " + _photos.Count + ".";
        }

        private void ClearPhotos_Click(object sender, RoutedEventArgs e)
        {
            _photos.Clear(); _placements.Clear(); _pageCount = 0; _currentPage = 0;
            DrawEmptyPage(); UpdatePageNavigation(); StatusText.Text = "Selección de fotografías vaciada.";
        }

        private void Settings_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && _photos.Count > 0) GenerateLayout_Click(this, new RoutedEventArgs());
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e) { if (_currentPage > 0) { _currentPage--; RenderCurrentPage(); } }
        private void NextPage_Click(object sender, RoutedEventArgs e) { if (_currentPage < _pageCount - 1) { _currentPage++; RenderCurrentPage(); } }

        private void GenerateLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_photos.Count == 0) { StatusText.Text = "Primero importa fotografías."; return; }
            double width, height, margin, gap;
            bool hasWidth = !String.IsNullOrWhiteSpace(PhotoWidthText.Text);
            bool hasHeight = !String.IsNullOrWhiteSpace(PhotoHeightText.Text);
            if ((hasWidth && !ReadPositive(PhotoWidthText.Text, out width)) ||
                (hasHeight && !ReadPositive(PhotoHeightText.Text, out height)) ||
                !ReadNonNegative(MarginText.Text, out margin) || !ReadNonNegative(GapText.Text, out gap))
            { MessageBox.Show("Revisa las medidas, márgenes y separación. Las dimensiones pueden quedar vacías, pero no contener valores inválidos.", "Configuración no válida"); return; }
            if (!hasWidth) width = 0;
            if (!hasHeight) height = 0;

            GetPaperSize(out _pageWidthCm, out _pageHeightCm);
            if (OrientationCombo.SelectedIndex == 1) { double temp = _pageWidthCm; _pageWidthCm = _pageHeightCm; _pageHeightCm = temp; }
            double usableW = _pageWidthCm - 2 * margin, usableH = _pageHeightCm - 2 * margin;
            if (usableW <= 0 || usableH <= 0) { MessageBox.Show("El margen es demasiado grande."); return; }

            _placements.Clear();
            bool keepAspect = AspectCombo.SelectedIndex == 0;
            if (hasWidth || hasHeight)
            {
                // Con una sola dimensión definida, cada foto calcula su otra medida con SU propia proporción.
                // Se empaquetan filas: ancho fijo llena columnas; alto fijo aprovecha el ancho disponible.
                int photoIndex = 0;
                int page = 0;
                double y = margin;
                while (photoIndex < _photos.Count)
                {
                    double x = margin;
                    double rowHeight = 0;
                    bool placedInRow = false;
                    while (photoIndex < _photos.Count)
                    {
                        PhotoItem photo = _photos[photoIndex];
                        double ratio = (double)photo.PixelWidth / Math.Max(1, photo.PixelHeight);
                        double cellW = hasWidth ? width : height * ratio;
                        double cellH = hasHeight ? height : width / ratio;
                        if (cellW > usableW || cellH > usableH)
                        {
                            MessageBox.Show("La fotografía " + photo.FileName + " no cabe en el papel con la medida indicada.");
                            return;
                        }
                        if (x > margin && x + cellW > margin + usableW + 0.0001) break;
                        if (y + Math.Max(rowHeight, cellH) > margin + usableH + 0.0001)
                        {
                            if (placedInRow) break;
                            page++;
                            y = margin;
                            x = margin;
                            rowHeight = 0;
                        }
                        _placements.Add(new PhotoPlacement { PhotoIndex = photoIndex, Page = page, X = x, Y = y, Width = cellW, Height = cellH });
                        x += cellW + gap;
                        rowHeight = Math.Max(rowHeight, cellH);
                        placedInRow = true;
                        photoIndex++;
                    }
                    if (placedInRow) y += rowHeight + gap;
                    else
                    {
                        page++;
                        y = margin;
                    }
                }
                _pageCount = page + 1;
            }
            else
            {
                int desiredPages;
                if (!Int32.TryParse(DesiredPagesText.Text, out desiredPages) || desiredPages < 1 || desiredPages > _photos.Count)
                { MessageBox.Show("Indica un número de hojas entre 1 y " + _photos.Count + "."); return; }
                _pageCount = desiredPages;
                int baseCount = _photos.Count / desiredPages, extra = _photos.Count % desiredPages;
                int maxPerPage = baseCount + (extra > 0 ? 1 : 0);
                int bestCols = 1, bestRows = maxPerPage;
                double averageRatio = 0;
                foreach (PhotoItem photo in _photos) averageRatio += (double)photo.PixelWidth / Math.Max(1, photo.PixelHeight);
                averageRatio /= _photos.Count;
                double bestScore = Double.NegativeInfinity;
                for (int cols = 1; cols <= maxPerPage; cols++)
                {
                    int rows = (int)Math.Ceiling((double)maxPerPage / cols);
                    double cellW = (usableW - gap * (cols - 1)) / cols;
                    double cellH = (usableH - gap * (rows - 1)) / rows;
                    if (cellW <= 0 || cellH <= 0) continue;
                    double cellRatio = cellW / cellH;
                    double ratioPenalty = Math.Abs(Math.Log(cellRatio / averageRatio));
                    double score = cellW * cellH / (1.0 + ratioPenalty * 0.35);
                    if (score > bestScore) { bestScore = score; bestCols = cols; bestRows = rows; }
                }
                double slotW = (usableW - gap * (bestCols - 1)) / bestCols;
                double slotH = (usableH - gap * (bestRows - 1)) / bestRows;
                int photoIndex = 0;
                for (int page = 0; page < desiredPages; page++)
                {
                    int count = baseCount + (page < extra ? 1 : 0);
                    for (int local = 0; local < count; local++)
                    {
                        int row = local / bestCols, col = local % bestCols;
                        _placements.Add(new PhotoPlacement { PhotoIndex = photoIndex++, Page = page,
                            X = margin + col * (slotW + gap), Y = margin + row * (slotH + gap), Width = slotW, Height = slotH });
                    }
                }
            }

            _currentPage = 0;
            RenderCurrentPage();
            StatusText.Text = _photos.Count + " fotografías distribuidas en " + _pageCount + " hoja(s). " +
                (hasWidth || hasHeight ? "Medida de referencia: " + (hasWidth ? width.ToString("0.##") + " cm de ancho" : height.ToString("0.##") + " cm de alto") + "." : "Reparto optimizado entre las hojas solicitadas.") +
                (keepAspect ? " Proporción original conservada." : " Ajuste exacto seleccionado.");
        }

        private void RenderCurrentPage()
        {
            double pageW = _pageWidthCm * Dpi / 2.54, pageH = _pageHeightCm * Dpi / 2.54;
            PreviewCanvas.Width = pageW; PreviewCanvas.Height = pageH; PreviewCanvas.Children.Clear(); PreviewCanvas.Background = Brushes.White;
            double scale = Dpi / 2.54;
            foreach (PhotoPlacement p in _placements)
            {
                if (p.Page != _currentPage) continue;
                var frame = new Border { Width = p.Width * scale, Height = p.Height * scale,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 205)), BorderThickness = new Thickness(0.5),
                    ClipToBounds = true, Background = Brushes.White };
                var image = new Image { Stretch = AspectCombo.SelectedIndex == 0 ? Stretch.Uniform : Stretch.Fill };
                var source = new BitmapImage();
                source.BeginInit(); source.UriSource = new Uri(_photos[p.PhotoIndex].FilePath); source.CacheOption = BitmapCacheOption.OnLoad; source.EndInit(); source.Freeze();
                image.Source = source; frame.Child = image;
                Canvas.SetLeft(frame, p.X * scale); Canvas.SetTop(frame, p.Y * scale); PreviewCanvas.Children.Add(frame);
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
        { return double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0; }
        private static bool ReadNonNegative(string text, out double value)
        { return double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0; }

        private void DrawEmptyPage()
        {
            PreviewCanvas.Width = 816; PreviewCanvas.Height = 1056; PreviewCanvas.Children.Clear();
            var hint = new TextBlock { Text = "Importa fotografías y genera una distribución", Foreground = Brushes.Gray, FontSize = 22 };
            PreviewCanvas.Children.Add(hint); Canvas.SetLeft(hint, 130); Canvas.SetTop(hint, 500);
        }
    }

    public class PhotoItem
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public BitmapImage Thumbnail { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }

    internal class PhotoPlacement
    {
        public int PhotoIndex { get; set; }
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
