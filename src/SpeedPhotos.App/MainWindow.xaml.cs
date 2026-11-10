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
        private int _selectedPlacementIndex = -1;
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
            _photos.Clear(); _placements.Clear(); _pageCount = 0; _currentPage = 0; _selectedPlacementIndex = -1;
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
            double width = 0, height = 0, margin, gap;
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
            _selectedPlacementIndex = -1;
            bool keepAspect = AspectCombo.SelectedIndex == 0;
            if (hasWidth || hasHeight)
            {
                var items = new List<LayoutItem>();
                for (int i = 0; i < _photos.Count; i++)
                {
                    PhotoItem photo = _photos[i];
                    double ratio = (double)photo.PixelWidth / Math.Max(1, photo.PixelHeight);
                    double cellW = hasWidth ? width : height * ratio;
                    double cellH = hasHeight ? height : width / ratio;
                    if (cellW > usableW + 0.0001 || cellH > usableH + 0.0001)
                    {
                        MessageBox.Show("La fotografía " + photo.FileName + " no cabe en el papel con la medida indicada.");
                        return;
                    }
                    items.Add(new LayoutItem { PhotoIndex = i, Width = cellW, Height = cellH });
                }

                // Multi-arranque: prueba distintos órdenes de colocación y conserva el mejor resultado.
                // La rotación se ofrece solo con ambas medidas fijas, para no incumplir una medida única.
                bool allowRotation = hasWidth && hasHeight && AllowRotationCheck.IsChecked == true;
                List<PhotoPlacement> bestLayout = null;
                int bestPageCount = Int32.MaxValue;
                int bestFragmentCount = Int32.MaxValue;
                for (int strategy = 0; strategy < 4; strategy++)
                {
                    var ordered = new List<LayoutItem>(items);
                    SortLayoutItems(ordered, strategy);
                    List<PhotoPlacement> candidate;
                    int candidatePages, fragments;
                    BuildMaxRectsLayout(ordered, usableW, usableH, margin, gap, allowRotation,
                        out candidate, out candidatePages, out fragments);
                    if (candidate == null)
                    {
                        MessageBox.Show("No fue posible distribuir todas las fotografías. Revisa las medidas y la separación.");
                        return;
                    }
                    if (candidatePages < bestPageCount ||
                        (candidatePages == bestPageCount && fragments < bestFragmentCount))
                    {
                        bestLayout = candidate;
                        bestPageCount = candidatePages;
                        bestFragmentCount = fragments;
                    }
                }
                _placements.AddRange(bestLayout);
                _pageCount = bestPageCount;
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

        private static void SortLayoutItems(List<LayoutItem> items, int strategy)
        {
            items.Sort(delegate(LayoutItem a, LayoutItem b)
            {
                double areaA = a.Width * a.Height, areaB = b.Width * b.Height;
                double sideA = Math.Max(a.Width, a.Height), sideB = Math.Max(b.Width, b.Height);
                double minA = Math.Min(a.Width, a.Height), minB = Math.Min(b.Width, b.Height);
                switch (strategy)
                {
                    case 0: return areaB.CompareTo(areaA);
                    case 1: return sideB.CompareTo(sideA);
                    case 2: return b.Height.CompareTo(a.Height);
                    default: return b.Width.CompareTo(a.Width);
                }
            });
        }

        private static void BuildMaxRectsLayout(List<LayoutItem> items, double pageW, double pageH,
            double margin, double gap, bool allowRotation, out List<PhotoPlacement> placements,
            out int pageCount, out int fragmentCount)
        {
            placements = new List<PhotoPlacement>();
            var pages = new List<List<FreeRectangle>>();
            foreach (LayoutItem item in items)
            {
                double normalW = item.Width + gap, normalH = item.Height + gap;
                double rotatedW = item.Height + gap, rotatedH = item.Width + gap;
                int bestPage = -1, bestRect = -1;
                bool bestRotated = false;
                double bestShort = Double.MaxValue, bestLong = Double.MaxValue, bestArea = Double.MaxValue;

                for (int p = 0; p < pages.Count; p++)
                {
                    for (int r = 0; r < pages[p].Count; r++)
                    {
                        FreeRectangle free = pages[p][r];
                        EvaluateFit(free, normalW, normalH, false, p, r,
                            ref bestPage, ref bestRect, ref bestRotated, ref bestShort, ref bestLong, ref bestArea);
                        if (allowRotation)
                            EvaluateFit(free, rotatedW, rotatedH, true, p, r,
                                ref bestPage, ref bestRect, ref bestRotated, ref bestShort, ref bestLong, ref bestArea);
                    }
                }

                if (bestPage < 0)
                {
                    pages.Add(new List<FreeRectangle> {
                        new FreeRectangle { X = margin, Y = margin, Width = pageW + gap, Height = pageH + gap }
                    });
                    bestPage = pages.Count - 1;
                    bestRect = 0;
                    FreeRectangle first = pages[bestPage][0];
                    bool normalFits = normalW <= first.Width + 0.0001 && normalH <= first.Height + 0.0001;
                    bool rotatedFits = allowRotation && rotatedW <= first.Width + 0.0001 && rotatedH <= first.Height + 0.0001;
                    if (!normalFits && !rotatedFits) { placements = null; pageCount = 0; fragmentCount = 0; return; }
                    bestRotated = !normalFits && rotatedFits;
                }

                FreeRectangle chosen = pages[bestPage][bestRect];
                double placedW = bestRotated ? item.Height : item.Width;
                double placedH = bestRotated ? item.Width : item.Height;
                var used = new FreeRectangle { X = chosen.X, Y = chosen.Y,
                    Width = placedW + gap, Height = placedH + gap };
                placements.Add(new PhotoPlacement { PhotoIndex = item.PhotoIndex, Page = bestPage,
                    X = chosen.X, Y = chosen.Y, Width = placedW, Height = placedH, Rotated = bestRotated });
                SplitFreeRectangles(pages[bestPage], used, used.Width, used.Height);
            }
            pageCount = pages.Count;
            fragmentCount = 0;
            foreach (List<FreeRectangle> page in pages) fragmentCount += page.Count;
        }

        private static void EvaluateFit(FreeRectangle free, double width, double height, bool rotated,
            int page, int rect, ref int bestPage, ref int bestRect, ref bool bestRotated,
            ref double bestShort, ref double bestLong, ref double bestArea)
        {
            if (width > free.Width + 0.0001 || height > free.Height + 0.0001) return;
            double leftoverW = free.Width - width, leftoverH = free.Height - height;
            double shortSide = Math.Min(leftoverW, leftoverH);
            double longSide = Math.Max(leftoverW, leftoverH);
            double area = free.Width * free.Height - width * height;
            if (shortSide < bestShort ||
                (Math.Abs(shortSide - bestShort) < 0.0001 && longSide < bestLong) ||
                (Math.Abs(shortSide - bestShort) < 0.0001 && Math.Abs(longSide - bestLong) < 0.0001 && area < bestArea))
            {
                bestShort = shortSide; bestLong = longSide; bestArea = area;
                bestPage = page; bestRect = rect; bestRotated = rotated;
            }
        }

        private static void SplitFreeRectangles(List<FreeRectangle> freeRects, FreeRectangle used, double usedW, double usedH)
        {
            var result = new List<FreeRectangle>();
            foreach (FreeRectangle free in freeRects)
            {
                if (used.X >= free.X + free.Width - 0.0001 ||
                    used.X + usedW <= free.X + 0.0001 ||
                    used.Y >= free.Y + free.Height - 0.0001 ||
                    used.Y + usedH <= free.Y + 0.0001)
                {
                    result.Add(free);
                    continue;
                }

                if (used.X > free.X + 0.0001)
                    result.Add(new FreeRectangle { X = free.X, Y = free.Y, Width = used.X - free.X, Height = free.Height });
                if (used.X + usedW < free.X + free.Width - 0.0001)
                    result.Add(new FreeRectangle { X = used.X + usedW, Y = free.Y, Width = free.X + free.Width - (used.X + usedW), Height = free.Height });
                if (used.Y > free.Y + 0.0001)
                    result.Add(new FreeRectangle { X = free.X, Y = free.Y, Width = free.Width, Height = used.Y - free.Y });
                if (used.Y + usedH < free.Y + free.Height - 0.0001)
                    result.Add(new FreeRectangle { X = free.X, Y = used.Y + usedH, Width = free.Width, Height = free.Y + free.Height - (used.Y + usedH) });
            }

            // Elimina espacios contenidos en otros para evitar evaluaciones redundantes.
            for (int i = result.Count - 1; i >= 0; i--)
            {
                bool contained = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (i == j) continue;
                    FreeRectangle a = result[i], b = result[j];
                    if (a.X >= b.X - 0.0001 && a.Y >= b.Y - 0.0001 &&
                        a.X + a.Width <= b.X + b.Width + 0.0001 &&
                        a.Y + a.Height <= b.Y + b.Height + 0.0001)
                    { contained = true; break; }
                }
                if (contained) result.RemoveAt(i);
            }
            freeRects.Clear();
            freeRects.AddRange(result);
        }

        private void RenderCurrentPage()
        {
            double pageW = _pageWidthCm * Dpi / 2.54, pageH = _pageHeightCm * Dpi / 2.54;
            PreviewCanvas.Width = pageW; PreviewCanvas.Height = pageH; PreviewCanvas.Children.Clear(); PreviewCanvas.Background = Brushes.White;
            double scale = Dpi / 2.54;
            for (int placementIndex = 0; placementIndex < _placements.Count; placementIndex++)
            {
                PhotoPlacement p = _placements[placementIndex];
                if (p.Page != _currentPage) continue;
                bool selected = placementIndex == _selectedPlacementIndex;
                var frame = new Border { Width = p.Width * scale, Height = p.Height * scale,
                    BorderBrush = selected ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromRgb(180, 190, 205)),
                    BorderThickness = selected ? new Thickness(2) : new Thickness(0.5),
                    ClipToBounds = true, Background = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Clic para seleccionar esta fotografía" };
                int capturedIndex = placementIndex;
                frame.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
                {
                    _selectedPlacementIndex = capturedIndex;
                    StatusText.Text = "Fotografía seleccionada. Puedes girarla o crear copias.";
                    RenderCurrentPage();
                    args.Handled = true;
                };
                var image = new Image { Stretch = AspectCombo.SelectedIndex == 0 ? Stretch.Uniform : Stretch.Fill };
                var source = new BitmapImage();
                source.BeginInit(); source.UriSource = new Uri(_photos[p.PhotoIndex].FilePath); source.CacheOption = BitmapCacheOption.OnLoad; source.EndInit(); source.Freeze();
                image.Source = source;
                if (p.Rotated) image.LayoutTransform = new RotateTransform(90);
                frame.Child = image;
                Canvas.SetLeft(frame, p.X * scale); Canvas.SetTop(frame, p.Y * scale); PreviewCanvas.Children.Add(frame);
            }
            UpdatePageNavigation();
        }


        private void RotateSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlacementIndex < 0 || _selectedPlacementIndex >= _placements.Count)
            {
                MessageBox.Show("Primero haz clic sobre una fotografía de la vista previa para seleccionarla.");
                return;
            }
            _placements[_selectedPlacementIndex].Rotated = !_placements[_selectedPlacementIndex].Rotated;
            RenderCurrentPage();
            StatusText.Text = "Fotografía girada 90°. Selección actualizada.";
        }

        private void DuplicateSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlacementIndex < 0 || _selectedPlacementIndex >= _placements.Count)
            {
                MessageBox.Show("Primero haz clic sobre una fotografía de la vista previa para seleccionarla.");
                return;
            }
            int copies;
            if (!Int32.TryParse(CopiesText.Text, out copies) || copies < 1 || copies > 50)
            {
                MessageBox.Show("Indica entre 1 y 50 copias.");
                return;
            }

            PhotoPlacement source = _placements[_selectedPlacementIndex];
            int added = 0;
            for (int copy = 0; copy < copies; copy++)
            {
                double x, y;
                int page;
                if (!FindFreePosition(source.Width, source.Height, out page, out x, out y))
                    break;
                var duplicate = new PhotoPlacement { PhotoIndex = source.PhotoIndex, Page = page,
                    X = x, Y = y, Width = source.Width, Height = source.Height, Rotated = source.Rotated };
                _placements.Add(duplicate);
                _selectedPlacementIndex = _placements.Count - 1;
                added++;
            }
            _pageCount = Math.Max(_pageCount, GetHighestPage() + 1);
            _currentPage = _placements[_selectedPlacementIndex].Page;
            RenderCurrentPage();
            StatusText.Text = added + " copia(s) agregada(s) de " + _photos[source.PhotoIndex].FileName +
                (added < copies ? ". No había espacio para todas; se creó una nueva hoja cuando fue necesario." : ".");
        }

        private bool FindFreePosition(double width, double height, out int page, out double x, out double y)
        {
            const double step = 0.2;
            double margin = 0;
            ReadNonNegative(MarginText.Text, out margin);
            double gap = 0;
            ReadNonNegative(GapText.Text, out gap);
            double right = _pageWidthCm - margin - width;
            double bottom = _pageHeightCm - margin - height;
            for (int p = 0; p < Math.Max(1, _pageCount + 1); p++)
            {
                for (double yy = margin; yy <= bottom + 0.0001; yy += step)
                {
                    for (double xx = margin; xx <= right + 0.0001; xx += step)
                    {
                        bool overlaps = false;
                        foreach (PhotoPlacement existing in _placements)
                        {
                            if (existing.Page != p) continue;
                            if (xx < existing.X + existing.Width + gap - 0.0001 &&
                                xx + width + gap > existing.X + 0.0001 &&
                                yy < existing.Y + existing.Height + gap - 0.0001 &&
                                yy + height + gap > existing.Y + 0.0001)
                            { overlaps = true; break; }
                        }
                        if (!overlaps) { page = p; x = xx; y = yy; return true; }
                    }
                }
            }
            page = 0; x = 0; y = 0; return false;
        }

        private int GetHighestPage()
        {
            int highest = -1;
            foreach (PhotoPlacement placement in _placements)
                if (placement.Page > highest) highest = placement.Page;
            return highest;
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

    internal class LayoutItem
    {
        public int PhotoIndex { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    internal class FreeRectangle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    internal class PhotoPlacement
    {
        public int PhotoIndex { get; set; }
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Rotated { get; set; }
    }
}
