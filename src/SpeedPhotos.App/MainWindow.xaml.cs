using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
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
            StatusText.Text = added + " fotografía(s) importada(s). Total: " + _photos.Count + ".";
        }

        private void ClearPhotos_Click(object sender, RoutedEventArgs e)
        {
            _photos.Clear();
            DrawEmptyPage();
            StatusText.Text = "Selección de fotografías vaciada.";
        }

        private void Settings_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && _photos.Count > 0) GenerateLayout_Click(this, new RoutedEventArgs());
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

            int requested = CountCombo.SelectedIndex == 0 ? int.MaxValue : int.Parse(((ComboBoxItem)CountCombo.SelectedItem).Content.ToString());
            int columns = 1, rows = 1, capacity = 0;
            double bestWaste = double.MaxValue;
            int upper = requested == int.MaxValue ? _photos.Count : Math.Min(_photos.Count, requested);

            // Busca la mayor cantidad que cabe y, para esa cantidad, la cuadrícula con menor espacio sobrante.
            for (int candidate = upper; candidate >= 1 && capacity == 0; candidate--)
            {
                for (int c = 1; c <= candidate; c++)
                {
                    int r = (int)Math.Ceiling((double)candidate / c);
                    double cellW = (usableWidth - gap * (c - 1)) / c;
                    if (cellW <= 0) continue;
                    double cellH = cellW * photoHeight / photoWidth;
                    if (cellH * r + gap * (r - 1) > usableHeight) continue;

                    double unused = (usableWidth * usableHeight) - (candidate * cellW * cellH);
                    if (unused < bestWaste)
                    {
                        bestWaste = unused;
                        columns = c;
                        rows = r;
                        capacity = candidate;
                    }
                }
                if (capacity == 0) bestWaste = double.MaxValue;
            }
            if (capacity == 0)
            {
                StatusText.Text = "No cabe ninguna fotografía con estas dimensiones y márgenes.";
                return;
            }

            double pageW = pageWidth * Dpi / 2.54;
            double pageH = pageHeight * Dpi / 2.54;
            PreviewCanvas.Width = pageW;
            PreviewCanvas.Height = pageH;
            PreviewCanvas.Children.Clear();
            PreviewCanvas.Background = Brushes.White;
            double xMargin = margin * Dpi / 2.54;
            double yMargin = xMargin;
            double gapDip = gap * Dpi / 2.54;
            double cellWidth = (usableWidth * Dpi / 2.54 - gapDip * (columns - 1)) / columns;
            double cellHeight = cellWidth * photoHeight / photoWidth;
            double gridHeight = rows * cellHeight + (rows - 1) * gapDip;
            double startY = (pageH - gridHeight) / 2.0;
            for (int i = 0; i < capacity; i++)
            {
                int row = i / columns;
                int col = i % columns;
                double x = xMargin + col * (cellWidth + gapDip);
                double y = startY + row * (cellHeight + gapDip);
                var frame = new Border
                {
                    Width = cellWidth, Height = cellHeight, BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 205)),
                    BorderThickness = new Thickness(0.5), ClipToBounds = true, Background = Brushes.White
                };
                var image = new Image { Stretch = Stretch.UniformToFill };
                var source = new BitmapImage();
                source.BeginInit(); source.UriSource = new Uri(_photos[i].FilePath);
                source.CacheOption = BitmapCacheOption.OnLoad; source.EndInit(); source.Freeze();
                image.Source = source;
                frame.Child = image;
                Canvas.SetLeft(frame, x); Canvas.SetTop(frame, y);
                PreviewCanvas.Children.Add(frame);
            }
            StatusText.Text = capacity + " fotografía(s) distribuidas en " + columns + " columna(s) × " + rows + " fila(s).";
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