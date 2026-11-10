using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SpeedPhotos.App
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<PhotoItem> _photos = new ObservableCollection<PhotoItem>();

        public MainWindow()
        {
            InitializeComponent();
            PhotosList.ItemsSource = _photos;
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
                    _photos.Add(new PhotoItem { FileName = Path.GetFileName(file), FilePath = file, Thumbnail = bitmap });
                    added++;
                }
                catch { /* Skip unsupported or damaged images. */ }
            }
            StatusText.Text = added + " fotografía(s) importada(s).";
        }

        private void ClearPhotos_Click(object sender, RoutedEventArgs e)
        {
            _photos.Clear();
            StatusText.Text = "Selección de fotografías vaciada.";
        }

        private void GenerateLayout_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = _photos.Count == 0
                ? "Primero importa al menos una fotografía."
                : "Fotografías listas. El motor de distribución se incorporará en la siguiente etapa.";
        }
    }

    public class PhotoItem
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public BitmapImage Thumbnail { get; set; }
    }
}
