using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Printing;
using System.Windows.Documents;

namespace SpeedPhotos.App
{
    public partial class MainWindow : Window
    {
        private const double Dpi = 96.0;
        private readonly ObservableCollection<PhotoItem> _photos = new ObservableCollection<PhotoItem>();
        private readonly List<PhotoPlacement> _placements = new List<PhotoPlacement>();
        private int _pageCount, _currentPage;
        private int _selectedPlacementIndex = -1;
        private bool _isDraggingPlacement;
        private double _dragOffsetX, _dragOffsetY, _dragOriginalX, _dragOriginalY;
        private double _pageWidthCm, _pageHeightCm;
        private Point _photoDragStart;
        private PhotoItem _photoDragItem;
        private PrintQueue _selectedPrinter;
        private PrintCapabilities _printerCapabilities;
        private bool _loadingPrinterOptions;

        public MainWindow()
        {
            InitializeComponent();
            PhotosList.ItemsSource = _photos;
            DrawEmptyPage();
            LoadPrinters();

        }

        private void DocumentPhoto_Click(object sender, RoutedEventArgs e)
        {
            var window = new DocumentPhotoWindow { Owner = this };
            window.ShowDialog();
        }

        private void ImportPhotos_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Seleccionar fotografías", Filter = "Archivos de imagen|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|Todos los archivos|*.*", Multiselect = true };
            if (dialog.ShowDialog() == true) AddPhotoFiles(dialog.FileNames);
        }

        private void MainWindow_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddPhotoFiles(files);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        public void ImportExternalFiles(IEnumerable<string> files)
        {
            AddPhotoFiles(files);
        }

        private void AddPhotoFiles(IEnumerable<string> files)
        {
            if (files == null) return;

            int added = 0;
            int rejected = 0;
            string firstError = null;
            var knownPaths = new HashSet<string>(
                _photos.Select(p => p.FilePath), StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                try
                {
                    if (String.IsNullOrWhiteSpace(file))
                    {
                        rejected++;
                        continue;
                    }

                    string fullPath = System.IO.Path.GetFullPath(file.Trim().Trim('"'));
                    if (!System.IO.File.Exists(fullPath))
                    {
                        rejected++;
                        if (firstError == null) firstError = "No existe el archivo: " + fullPath;
                        continue;
                    }

                    string extension = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
                    if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" &&
                        extension != ".bmp" && extension != ".tif" && extension != ".tiff")
                    {
                        rejected++;
                        if (firstError == null) firstError = "Formato no compatible: " + fullPath;
                        continue;
                    }

                    if (!knownPaths.Add(fullPath))
                        continue;

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 180;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    _photos.Add(new PhotoItem
                    {
                        FileName = System.IO.Path.GetFileName(fullPath),
                        FilePath = fullPath,
                        Thumbnail = bitmap,
                        PixelWidth = bitmap.PixelWidth,
                        PixelHeight = bitmap.PixelHeight
                    });
                    added++;
                }
                catch (Exception ex)
                {
                    rejected++;
                    if (firstError == null)
                        firstError = file + Environment.NewLine + ex.Message;
                }
            }

            if (added > 0)
            {
                StatusText.Text = added + " fotografía(s) importada(s). Total: " + _photos.Count +
                    (rejected > 0 ? ". " + rejected + " archivo(s) no se pudieron importar." : ".");
            }
            else if (rejected > 0)
            {
                StatusText.Text = "No se pudo importar ninguna fotografía.";
                MessageBox.Show(this, "No se pudieron importar los archivos recibidos desde Windows.\n\n" +
                    (firstError ?? "Comprueba que los archivos sean imágenes compatibles y que estén disponibles."),
                    "SpeedPhotos - Error al importar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void TogglePrintSettings_Click(object sender, RoutedEventArgs e)
        {
            PrintSettingsPanel.Visibility = PrintSettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            try
            {
                StatusText.Text = "Buscando actualizaciones...";
                UpdateInfo update = await UpdateService.CheckForUpdateAsync();
                if (update == null)
                {
                    MessageBox.Show(this, "Ya tienes instalada la versión más reciente de SpeedPhotos.",
                        "SpeedPhotos - Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusText.Text = "SpeedPhotos está actualizado.";
                    return;
                }

                Version current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string message = "Hay una nueva versión disponible.\n\nVersión instalada: " + current +
                    "\nNueva versión: " + update.Version + "\n\n¿Deseas descargar e instalar la actualización?";
                if (MessageBox.Show(this, message, "Actualización disponible",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    StatusText.Text = "Actualización pospuesta.";
                    return;
                }

                StatusText.Text = "Descargando actualización...";
                string installerPath = await UpdateService.DownloadInstallerAsync(update);
                StatusText.Text = "Iniciando instalador...";
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(start);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = "No se pudo completar la actualización.";
                MessageBox.Show(this, "No fue posible buscar o instalar actualizaciones.\n\n" + ex.Message,
                    "SpeedPhotos - Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private void LoadPrinters()
        {
            _loadingPrinterOptions = true;
            PrinterCombo.Items.Clear();
            try
            {
                var server = new LocalPrintServer();
                string defaultPrinterName = null;
                try
                {
                    PrintQueue defaultQueue = server.DefaultPrintQueue;
                    if (defaultQueue != null) defaultPrinterName = defaultQueue.FullName;
                }
                catch { }

                var queues = new Dictionary<string, PrintQueue>(StringComparer.OrdinalIgnoreCase);

                // Enumerar colas locales y conexiones de red/compartidas registradas en Windows.
                try
                {
                    foreach (PrintQueue queue in server.GetPrintQueues(new[]
                    {
                        EnumeratedPrintQueueTypes.Local,
                        EnumeratedPrintQueueTypes.Connections
                    }))
                    {
                        try
                        {
                            queue.Refresh();
                            string key = queue.FullName;
                            if (!String.IsNullOrWhiteSpace(key) && !queues.ContainsKey(key))
                                queues.Add(key, queue);
                        }
                        catch { }
                    }
                }
                catch { }

                // Respaldo: la lista de impresoras instaladas que expone Windows.
                // Esto ayuda a incluir colas USB, compartidas y conexiones que el servidor
                // de impresión no haya devuelto en la primera enumeración.
                try
                {
                    foreach (string installedName in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    {
                        if (String.IsNullOrWhiteSpace(installedName)) continue;
                        bool exists = queues.Keys.Any(k =>
                            String.Equals(k, installedName, StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(k, installedName.TrimStart('\\'), StringComparison.OrdinalIgnoreCase));
                        if (exists) continue;

                        try
                        {
                            PrintQueue queue = server.GetPrintQueue(installedName);
                            queue.Refresh();
                            string key = queue.FullName;
                            if (!String.IsNullOrWhiteSpace(key) && !queues.ContainsKey(key))
                                queues.Add(key, queue);
                        }
                        catch
                        {
                            // Una cola puede estar desconectada o no permitir consultar
                            // sus capacidades; se conserva el resto de impresoras.
                        }
                    }
                }
                catch { }

                foreach (PrintQueue queue in queues.Values.OrderBy(q => q.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var item = new ComboBoxItem
                    {
                        Content = String.IsNullOrWhiteSpace(queue.Name) ? queue.FullName : queue.Name,
                        ToolTip = queue.FullName,
                        Tag = queue
                    };
                    PrinterCombo.Items.Add(item);
                    if (!String.IsNullOrWhiteSpace(defaultPrinterName) &&
                        String.Equals(queue.FullName, defaultPrinterName, StringComparison.OrdinalIgnoreCase))
                        PrinterCombo.SelectedItem = item;
                }

                if (PrinterCombo.SelectedIndex < 0 && PrinterCombo.Items.Count > 0)
                    PrinterCombo.SelectedIndex = 0;

                if (PrinterCombo.Items.Count == 0)
                    PrinterStatusText.Text = "Windows no reporta impresoras instaladas. Comprueba que estén agregadas en Configuración de Windows.";
            }
            catch (Exception ex)
            {
                PrinterStatusText.Text = "No se pudieron consultar las impresoras instaladas en Windows: " + ex.Message;
            }
            _loadingPrinterOptions = false;
            if (PrinterCombo.SelectedItem != null) PrinterCombo_SelectionChanged(this, null);
        }

        private void PrinterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingPrinterOptions) return;
            var item = PrinterCombo.SelectedItem as ComboBoxItem;
            _selectedPrinter = item == null ? null : item.Tag as PrintQueue;
            LoadPrinterCapabilities();
        }

        private void LoadPrinterCapabilities()
        {
            ClearPrinterOptions();
            if (_selectedPrinter == null) return;
            try
            {
                _selectedPrinter.Refresh();
                _printerCapabilities = _selectedPrinter.GetPrintCapabilities();
                AddPaperSizeOptions(_printerCapabilities.PageMediaSizeCapability);
                AddCapabilityOptions(PaperTypeCombo, _printerCapabilities.PageMediaTypeCapability);
                AddPrintQualityOptions(_printerCapabilities);
                AddDuplexOptions(_printerCapabilities.DuplexingCapability);
                AddCapabilityOptions(ColorModeCombo, _printerCapabilities.OutputColorCapability);
                PaperCombo.IsEnabled = PaperSizeCombo.Items.Count == 0;
                PrintTicket defaultTicket = _selectedPrinter.DefaultPrintTicket ?? new PrintTicket();
                SetDefaultCapability(PaperSizeCombo, defaultTicket.PageMediaSize);
                SetDefaultCapability(PaperTypeCombo, defaultTicket.PageMediaType);
                SetDefaultCapability(PrintQualityCombo, defaultTicket.PageResolution);
                SetDefaultCapability(DuplexCombo, defaultTicket.Duplexing);
                SetDefaultCapability(ColorModeCombo, defaultTicket.OutputColor);
                if (PaperSizeCombo.SelectedIndex < 0 && PaperSizeCombo.Items.Count > 0) PaperSizeCombo.SelectedIndex = 0;
                if (PaperTypeCombo.SelectedIndex < 0 && PaperTypeCombo.Items.Count > 0) PaperTypeCombo.SelectedIndex = 0;
                if (PrintQualityCombo.SelectedIndex < 0 && PrintQualityCombo.Items.Count > 0) PrintQualityCombo.SelectedIndex = 0;
                PrintQualityCombo.IsEnabled = PrintQualityCombo.Items.Count > 0;
                if (ColorModeCombo.SelectedIndex < 0 && ColorModeCombo.Items.Count > 0) ColorModeCombo.SelectedIndex = 0;
                bool supportsDuplex = _printerCapabilities.DuplexingCapability != null &&
                    (_printerCapabilities.DuplexingCapability.Contains(Duplexing.TwoSidedLongEdge) ||
                     _printerCapabilities.DuplexingCapability.Contains(Duplexing.TwoSidedShortEdge));
                DuplexCheck.IsEnabled = supportsDuplex;
                DuplexCheck.IsChecked = false;
                DuplexCombo.IsEnabled = false;
                if (!supportsDuplex) DuplexCombo.Items.Clear();
                string status = "Opciones obtenidas del controlador de " + _selectedPrinter.Name + ".";
                if (!supportsDuplex)
                    status += " No informa compatibilidad con impresión a doble cara.";
                if (PrintQualityCombo.Items.Count == 0)
                    status += " El controlador no publica niveles ni resoluciones de calidad.";
                PrinterStatusText.Text = status;
                UpdatePreviewColorMode();
            }
            catch (Exception ex)
            {
                PrinterStatusText.Text = "No se pudieron consultar las capacidades: " + ex.Message;
            }
        }

        private static void AddCapabilityOptions(ComboBox combo, System.Collections.IEnumerable values)
        {
            combo.Items.Clear();
            if (values == null) return;
            foreach (object value in values)
            {
                combo.Items.Add(new ComboBoxItem { Content = value.ToString(), Tag = value });
            }
        }

        // Primero consulta la capacidad WPF del controlador. Si viene vacía,
        // utiliza la lista GDI de resoluciones que Windows expone para esa impresora.
        private void AddPrintQualityOptions(PrintCapabilities capabilities)
        {
            PrintQualityCombo.Items.Clear();
            if (capabilities != null)
            {
                IList<PageResolution> resolutions = capabilities.PageResolutionCapability;
                if (resolutions != null && resolutions.Count > 0)
                {
                    foreach (PageResolution resolution in resolutions)
                    {
                        if (resolution == null) continue;
                        string label = resolution.X.HasValue && resolution.Y.HasValue
                            ? resolution.X.Value + " × " + resolution.Y.Value + " ppp"
                            : resolution.ToString();
                        PrintQualityCombo.Items.Add(new ComboBoxItem { Content = label, Tag = resolution });
                    }
                    if (PrintQualityCombo.Items.Count > 0) return;
                }
            }

            try
            {
                var settings = new System.Drawing.Printing.PrinterSettings
                {
                    PrinterName = _selectedPrinter.Name
                };
                if (!settings.IsValid)
                    settings.PrinterName = _selectedPrinter.FullName;

                foreach (System.Drawing.Printing.PrinterResolution resolution in settings.PrinterResolutions)
                {
                    PageResolution ticketResolution = null;
                    string label = null;

                    switch (resolution.Kind)
                    {
                        case System.Drawing.Printing.PrinterResolutionKind.High:
                            label = "Alta";
                            ticketResolution = new PageResolution(PageQualitativeResolution.High);
                            break;
                        case System.Drawing.Printing.PrinterResolutionKind.Medium:
                            label = "Media / Normal";
                            ticketResolution = new PageResolution(PageQualitativeResolution.Normal);
                            break;
                        case System.Drawing.Printing.PrinterResolutionKind.Low:
                            label = "Baja";
                            ticketResolution = new PageResolution(PageQualitativeResolution.Draft);
                            break;
                        case System.Drawing.Printing.PrinterResolutionKind.Draft:
                            label = "Borrador";
                            ticketResolution = new PageResolution(PageQualitativeResolution.Draft);
                            break;
                        case System.Drawing.Printing.PrinterResolutionKind.Custom:
                            if (resolution.X > 0 && resolution.Y > 0)
                            {
                                label = resolution.X + " × " + resolution.Y + " ppp";
                                ticketResolution = new PageResolution(resolution.X, resolution.Y);
                            }
                            break;
                    }

                    if (ticketResolution != null)
                        PrintQualityCombo.Items.Add(new ComboBoxItem { Content = label, Tag = ticketResolution });
                }
            }
            catch
            {
                // Algunos controladores no publican resoluciones mediante GDI.
            }
        }

        private void AddPaperSizeOptions(IList<PageMediaSize> sizes)
        {
            PaperSizeCombo.Items.Clear();
            if (sizes == null) return;

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PageMediaSize size in sizes)
            {
                if (size == null) continue;
                string label = FormatPaperSize(size);
                string key = size.PageMediaSizeName.HasValue
                    ? size.PageMediaSizeName.Value.ToString()
                    : label;
                if (size.Width.HasValue && size.Height.HasValue)
                    key += "|" + Math.Round(size.Width.Value, 1) + "|" + Math.Round(size.Height.Value, 1);
                if (!unique.Add(key)) continue;

                PaperSizeCombo.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = size,
                    ToolTip = label
                });
            }
        }

        private static string FormatPaperSize(PageMediaSize size)
        {
            string name = size.PageMediaSizeName.HasValue
                ? size.PageMediaSizeName.Value.ToString()
                : String.Empty;

            var friendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ISOA0", "A0" }, { "ISOA1", "A1" }, { "ISOA2", "A2" },
                { "ISOA3", "A3" }, { "ISOA4", "A4" }, { "ISOA5", "A5" },
                { "ISOA6", "A6" }, { "ISOA7", "A7" }, { "ISOA8", "A8" },
                { "ISOA9", "A9" }, { "ISOA10", "A10" },
                { "ISOB0", "B0" }, { "ISOB1", "B1" }, { "ISOB2", "B2" },
                { "ISOB3", "B3" }, { "ISOB4", "B4" }, { "ISOB5", "B5" },
                { "ISOB6", "B6" }, { "ISOB7", "B7" }, { "ISOB8", "B8" },
                { "ISOB9", "B9" }, { "ISOB10", "B10" },
                { "JISB0", "JIS B0" }, { "JISB1", "JIS B1" }, { "JISB2", "JIS B2" },
                { "JISB3", "JIS B3" }, { "JISB4", "JIS B4" }, { "JISB5", "JIS B5" },
                { "JISB6", "JIS B6" }, { "NorthAmericaLetter", "Carta (Letter)" },
                { "NorthAmericaLegal", "Legal" }, { "NorthAmericaTabloid", "Tabloide" },
                { "NorthAmericaExecutive", "Ejecutivo" }, { "NorthAmericaStatement", "Statement" },
                { "NorthAmericaLetterPlus", "Carta Plus" }, { "NorthAmericaSuperA", "Super A" },
                { "NorthAmericaSuperB", "Super B" }, { "NorthAmericaPostcard", "Postal" },
                { "NorthAmericaNumber10Envelope", "Sobre n.º 10" },
                { "NorthAmericaMonarchEnvelope", "Sobre Monarch" },
                { "NorthAmericaPersonalEnvelope", "Sobre Personal" }
            };

            string displayName;
            if (!friendlyNames.TryGetValue(name, out displayName))
                displayName = String.IsNullOrWhiteSpace(name)
                    ? "Tamaño personalizado"
                    : name.Replace("NorthAmerica", "").Replace("ISO", "").Replace("JIS", "JIS ");

            if (size.Width.HasValue && size.Height.HasValue)
            {
                double widthMm = size.Width.Value * 25.4 / Dpi;
                double heightMm = size.Height.Value * 25.4 / Dpi;
                return displayName + " — " + Math.Round(widthMm) + " × " +
                    Math.Round(heightMm) + " mm";
            }

            return displayName;
        }

        private static void SetDefaultCapability(ComboBox combo, object value)
        {
            if (value == null) return;
            foreach (ComboBoxItem item in combo.Items)
                if (Object.Equals(item.Tag, value)) { combo.SelectedItem = item; return; }
        }

        private void ClearPrinterOptions()
        {
            PaperSizeCombo.Items.Clear(); PaperTypeCombo.Items.Clear(); PrintQualityCombo.Items.Clear();
            PrintQualityCombo.IsEnabled = false;
            DuplexCombo.Items.Clear(); ColorModeCombo.Items.Clear();
            PaperCombo.IsEnabled = true;
            DuplexCheck.IsChecked = false; DuplexCheck.IsEnabled = false; DuplexCombo.IsEnabled = false;
        }

        private bool IsGrayscaleSelected()
        {
            var item = ColorModeCombo == null ? null : ColorModeCombo.SelectedItem as ComboBoxItem;
            return item != null && item.Tag is OutputColor &&
                ((OutputColor)item.Tag == OutputColor.Grayscale || (OutputColor)item.Tag == OutputColor.Monochrome);
        }

        private void DuplexCheck_Changed(object sender, RoutedEventArgs e)
        {
            DuplexCombo.IsEnabled = DuplexCheck.IsEnabled && DuplexCheck.IsChecked == true;
        }

        private void ColorModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) UpdatePreviewColorMode();
        }

        private void UpdatePreviewColorMode()
        {
            if (_pageCount > 0) RenderCurrentPage();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_placements.Count == 0 || _pageCount == 0)
            {
                MessageBox.Show(this, "Primero importa fotografías y genera una distribución.",
                    "Exportar composición", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Exportar composición",
                Filter = "Documento PDF (*.pdf)|*.pdf|Imágenes JPEG (*.jpg)|*.jpg",
                FilterIndex = 1,
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = "SpeedPhotos"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var exportPlacements = _placements.Select(p => new ExportPhotoPlacement
                {
                    PhotoIndex = p.PhotoIndex,
                    Page = p.Page,
                    X = p.X,
                    Y = p.Y,
                    Width = p.Width,
                    Height = p.Height,
                    Rotated = p.Rotated
                }).ToList();
                var photoPaths = _photos.Select(p => p.FilePath).ToList();

                if (dialog.FilterIndex == 1)
                {
                    ExportService.ExportPdf(dialog.FileName, _pageWidthCm, _pageHeightCm,
                        _pageCount, exportPlacements, photoPaths,
                        AspectCombo.SelectedIndex == 0, IsGrayscaleSelected());
                    StatusText.Text = "Composición exportada a PDF: " + dialog.FileName;
                    MessageBox.Show(this, "El PDF se creó correctamente con " + _pageCount +
                        " hoja(s).", "Exportación completada", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    List<string> files = ExportService.ExportJpegPages(dialog.FileName,
                        _pageWidthCm, _pageHeightCm, _pageCount, exportPlacements,
                        photoPaths, AspectCombo.SelectedIndex == 0, IsGrayscaleSelected());
                    StatusText.Text = files.Count + " imagen(es) JPG exportada(s).";
                    MessageBox.Show(this, "Se exportaron " + files.Count +
                        " imagen(es) JPG en la carpeta seleccionada.", "Exportación completada",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo exportar la composición.\n\nDetalle: " + ex.Message,
                    "Error de exportación", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintNow_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPrinter == null) { MessageBox.Show("No hay una impresora seleccionada."); return; }
            if (_placements.Count == 0 || _pageCount == 0) { MessageBox.Show("Primero genera una distribución de fotografías."); return; }
            try
            {
                var ticket = _selectedPrinter.DefaultPrintTicket ?? new PrintTicket();
                var size = SelectedCapability<PageMediaSize>(PaperSizeCombo);
                var type = SelectedEnumCapability<PageMediaType>(PaperTypeCombo);
                var resolution = SelectedCapability<PageResolution>(PrintQualityCombo);
                if (size != null) ticket.PageMediaSize = size;
                ticket.PageOrientation = OrientationCombo.SelectedIndex == 1 ? PageOrientation.Landscape : PageOrientation.Portrait;
                if (type.HasValue) ticket.PageMediaType = type.Value;
                if (resolution != null) ticket.PageResolution = resolution;
                var color = SelectedEnumCapability<OutputColor>(ColorModeCombo);
                if (color.HasValue) ticket.OutputColor = color.Value;
                if (DuplexCheck.IsChecked == true)
                {
                    var duplex = SelectedEnumCapability<Duplexing>(DuplexCombo);
                    if (duplex.HasValue && (duplex.Value == Duplexing.TwoSidedLongEdge || duplex.Value == Duplexing.TwoSidedShortEdge))
                        ticket.Duplexing = duplex.Value;
                }
                else ticket.Duplexing = Duplexing.OneSided;

                var dialog = new PrintDialog { PrintQueue = _selectedPrinter, PrintTicket = ticket };
                var document = new FixedDocument();
                double pageW = _pageWidthCm * Dpi / 2.54, pageH = _pageHeightCm * Dpi / 2.54;
                for (int pageIndex = 0; pageIndex < _pageCount; pageIndex++)
                {
                    var fixedPage = new FixedPage { Width = pageW, Height = pageH };
                    var canvas = new Canvas { Width = pageW, Height = pageH, Background = Brushes.White };
                    foreach (PhotoPlacement placement in _placements)
                    {
                        if (placement.Page != pageIndex) continue;
                        var image = new Image { Width = placement.Width * Dpi / 2.54,
                            Height = placement.Height * Dpi / 2.54,
                            Stretch = AspectCombo.SelectedIndex == 0 ? Stretch.Uniform : Stretch.Fill };
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.UriSource = new Uri(_photos[placement.PhotoIndex].FilePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                        image.Source = IsGrayscaleSelected()
                            ? new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0) : (ImageSource)bitmap;
                        if (placement.Rotated) image.LayoutTransform = new RotateTransform(90);
                        var frame = new Border { Width = image.Width, Height = image.Height, Child = image,
                            ClipToBounds = true, Background = Brushes.White };
                        Canvas.SetLeft(frame, placement.X * Dpi / 2.54);
                        Canvas.SetTop(frame, placement.Y * Dpi / 2.54);
                        canvas.Children.Add(frame);
                    }
                    fixedPage.Children.Add(canvas);
                    var content = new PageContent();
                    content.Child = fixedPage;
                    document.Pages.Add(content);
                }
                dialog.PrintDocument(document.DocumentPaginator, "SpeedPhotos");
                PrinterStatusText.Text = "Trabajo de impresión enviado a " + _selectedPrinter.Name + ".";
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo imprimir. Revisa las opciones del controlador. Detalle: " + ex.Message,
                    "Error de impresión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static T SelectedCapability<T>(ComboBox combo) where T : class
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item == null ? null : item.Tag as T;
        }

        private static Nullable<T> SelectedEnumCapability<T>(ComboBox combo) where T : struct
        {
            var item = combo.SelectedItem as ComboBoxItem;
            if (item == null || !(item.Tag is T)) return null;
            return (T)item.Tag;
        }

        private void AddDuplexOptions(IList<Duplexing> capabilities)
        {
            DuplexCombo.Items.Clear();
            if (capabilities == null) return;
            foreach (Duplexing mode in capabilities)
            {
                if (mode == Duplexing.TwoSidedLongEdge)
                    DuplexCombo.Items.Add(new ComboBoxItem { Content = "Doble cara - borde largo", Tag = mode });
                else if (mode == Duplexing.TwoSidedShortEdge)
                    DuplexCombo.Items.Add(new ComboBoxItem { Content = "Doble cara - borde corto", Tag = mode });
            }
            if (DuplexCombo.Items.Count > 0) DuplexCombo.SelectedIndex = 0;
        }

        private void PrinterPaperSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingPrinterOptions || !IsLoaded) return;
            PageMediaSize media = SelectedCapability<PageMediaSize>(PaperSizeCombo);
            if (media != null && media.Width.HasValue && media.Height.HasValue && _photos.Count > 0)
                GenerateLayout_Click(this, new RoutedEventArgs());
        }

        private void PhotosList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _photoDragStart = e.GetPosition(PhotosList);
            _photoDragItem = null;
            var item = ItemsControl.ContainerFromElement(PhotosList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null) _photoDragItem = item.DataContext as PhotoItem;
        }

        private void PhotosList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _photoDragItem == null) return;
            Point current = e.GetPosition(PhotosList);
            if (Math.Abs(current.X - _photoDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _photoDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            PhotoItem dragged = _photoDragItem;
            _photoDragItem = null;
            var data = new DataObject(typeof(PhotoItem), dragged);
            DragDrop.DoDragDrop(PhotosList, data, DragDropEffects.Move);
        }

        private void PhotosList_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(PhotoItem)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void PhotosList_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(PhotoItem))) return;
            PhotoItem dragged = e.Data.GetData(typeof(PhotoItem)) as PhotoItem;
            if (dragged == null) return;

            int oldIndex = _photos.IndexOf(dragged);
            if (oldIndex < 0) return;

            var target = ItemsControl.ContainerFromElement(PhotosList, e.OriginalSource as DependencyObject) as ListBoxItem;
            int newIndex = target == null ? _photos.Count - 1 : PhotosList.ItemContainerGenerator.IndexFromContainer(target);
            if (newIndex < 0) newIndex = _photos.Count - 1;

            if (oldIndex != newIndex)
            {
                _photos.Move(oldIndex, newIndex);
                if (_placements.Count > 0)
                    GenerateLayout_Click(this, new RoutedEventArgs());
                else
                    StatusText.Text = "Orden de fotografías actualizado.";
            }
            e.Handled = true;
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
            double width = 0, height = 0, margin = 0, gap = 0;
            bool hasWidth = false;
            bool hasHeight = false;
            string invalidField = null;
            if (!String.IsNullOrWhiteSpace(PhotoWidthText.Text))
            {
                if (!TryReadNumber(PhotoWidthText.Text, out width) || width < 0) invalidField = "Ancho de fotografía";
                else hasWidth = width > 0;
            }
            if (invalidField == null && !String.IsNullOrWhiteSpace(PhotoHeightText.Text))
            {
                if (!TryReadNumber(PhotoHeightText.Text, out height) || height < 0) invalidField = "Alto de fotografía";
                else hasHeight = height > 0;
            }
            if (invalidField == null && !ReadNonNegative(MarginText.Text, out margin)) invalidField = "Margen de hoja";
            else if (invalidField == null && !ReadNonNegative(GapText.Text, out gap)) invalidField = "Separación";
            if (invalidField != null)
            {
                MessageBox.Show("El valor del campo '" + invalidField + "' no es válido. Usa un número mayor que cero para ancho y alto, y un número igual o mayor que cero para margen y separación. Puedes usar coma o punto decimal.", "Configuración no válida");
                return;
            }
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
                bool allowRotation = AllowRotationCheck.IsChecked == true;
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
                double averageRatio = 1.0;
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
                    ClipToBounds = true, Background = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand };
                System.Windows.Controls.Panel.SetZIndex(frame, selected ? 10000 : 0);
                int capturedIndex = placementIndex;
                frame.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
                {
                    _selectedPlacementIndex = capturedIndex;
                    _isDraggingPlacement = false;
                    System.Windows.Point pointer = args.GetPosition(PreviewCanvas);
                    _dragOffsetX = pointer.X - p.X * scale;
                    _dragOffsetY = pointer.Y - p.Y * scale;
                    _dragOriginalX = p.X; _dragOriginalY = p.Y;
                    frame.BorderBrush = Brushes.DodgerBlue; frame.BorderThickness = new Thickness(2);
                    System.Windows.Controls.Panel.SetZIndex(frame, 10000);
                    SelectionToolbar.Visibility = Visibility.Visible;
                    frame.CaptureMouse(); args.Handled = true;
                };
                frame.MouseMove += delegate(object sender, System.Windows.Input.MouseEventArgs args)
                {
                    if (!frame.IsMouseCaptured || args.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                    System.Windows.Point pointer = args.GetPosition(PreviewCanvas);
                    double nx = (pointer.X - _dragOffsetX) / scale, ny = (pointer.Y - _dragOffsetY) / scale;
                    if (Math.Abs(nx - _dragOriginalX) > 0.03 || Math.Abs(ny - _dragOriginalY) > 0.03) _isDraggingPlacement = true;
                    if (_isDraggingPlacement) { Canvas.SetLeft(frame, nx * scale); Canvas.SetTop(frame, ny * scale); }
                };
                frame.MouseLeftButtonUp += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
                {
                    // Lee la posición antes de liberar la captura del ratón.
                    double nx = Canvas.GetLeft(frame) / scale;
                    double ny = Canvas.GetTop(frame) / scale;
                    if (frame.IsMouseCaptured) frame.ReleaseMouseCapture();

                    if (_isDraggingPlacement)
                    {
                        double margin = 0, gap = 0;
                        ReadNonNegative(MarginText.Text, out margin);
                        ReadNonNegative(GapText.Text, out gap);

                        // Limita el movimiento al área imprimible en lugar de cancelar
                        // el arrastre cuando el usuario suelta cerca del borde inferior o lateral.
                        double maxX = _pageWidthCm - margin - p.Width;
                        double maxY = _pageHeightCm - margin - p.Height;
                        if (maxX < margin) maxX = margin;
                        if (maxY < margin) maxY = margin;
                        double boundedX = Math.Max(margin, Math.Min(nx, maxX));
                        double boundedY = Math.Max(margin, Math.Min(ny, maxY));
                        bool wasLimited = Math.Abs(boundedX - nx) > 0.0001 ||
                            Math.Abs(boundedY - ny) > 0.0001;

                        p.X = boundedX;
                        p.Y = boundedY;
                        ReflowPageAfterDrop(capturedIndex, margin, gap);
                        StatusText.Text = wasLimited
                            ? "Fotografía ajustada al margen de la hoja y reubicada. Se reorganizaron las demás posiciones disponibles."
                            : "Fotografía reubicada. Se reorganizaron las demás posiciones disponibles.";
                    }

                    _isDraggingPlacement = false;
                    RenderCurrentPage();
                    args.Handled = true;
                };
                                var image = new Image { Stretch = AspectCombo.SelectedIndex == 0 ? Stretch.Uniform : Stretch.Fill };
                var source = new BitmapImage();
                source.BeginInit(); source.UriSource = new Uri(_photos[p.PhotoIndex].FilePath); source.CacheOption = BitmapCacheOption.OnLoad; source.EndInit(); source.Freeze();
                image.Source = IsGrayscaleSelected() ? new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0) : (ImageSource)source;
                if (p.Rotated) image.LayoutTransform = new RotateTransform(90);
                frame.Child = image;
                Canvas.SetLeft(frame, p.X * scale); Canvas.SetTop(frame, p.Y * scale); PreviewCanvas.Children.Add(frame);
            }
            UpdatePageNavigation();
            UpdateSelectionToolbar();
        }

        private void UpdateSelectionToolbar()
        {
            bool selected = _selectedPlacementIndex >= 0 && _selectedPlacementIndex < _placements.Count && _placements[_selectedPlacementIndex].Page == _currentPage;
            SelectionToolbar.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            MovePageCombo.Items.Clear();
            for (int i = 0; i < _pageCount; i++) MovePageCombo.Items.Add("Hoja " + (i + 1));
            MovePageCombo.Items.Add("Nueva hoja");
            if (MovePageCombo.Items.Count > 0) MovePageCombo.SelectedIndex = selected ? _placements[_selectedPlacementIndex].Page : _currentPage;
        }

        // Mantiene fija la fotografía soltada y recoloca las demás de su hoja.
        // Para cada foto intenta primero avanzar a la derecha; después busca filas inferiores.
        // Si una foto no encuentra espacio, no revierte el movimiento principal.
        private bool ReflowPageAfterDrop(int selectedIndex, double margin, double gap)
        {
            if (selectedIndex < 0 || selectedIndex >= _placements.Count) return false;

            PhotoPlacement selected = _placements[selectedIndex];
            double rightLimit = _pageWidthCm - margin;
            double bottomLimit = _pageHeightCm - margin;
            var ordered = new List<int>();

            for (int i = 0; i < _placements.Count; i++)
                if (i != selectedIndex && _placements[i].Page == selected.Page) ordered.Add(i);

            ordered.Sort(delegate(int a, int b)
            {
                int row = _placements[a].Y.CompareTo(_placements[b].Y);
                return row != 0 ? row : _placements[a].X.CompareTo(_placements[b].X);
            });

            var placed = new List<int>();
            placed.Add(selectedIndex);
            const double step = 0.2;

            foreach (int index in ordered)
            {
                PhotoPlacement current = _placements[index];
                double originalX = current.X;
                double originalY = current.Y;
                bool found = IsValidReflowPosition(current, placed, originalX, originalY,
                    margin, gap, rightLimit, bottomLimit);

                // Primera prioridad: desplazar hacia la derecha conservando la fila.
                if (!found)
                {
                    for (double x = originalX + step; x + current.Width <= rightLimit + 0.0001; x += step)
                    {
                        if (IsValidReflowPosition(current, placed, x, originalY,
                            margin, gap, rightLimit, bottomLimit))
                        {
                            current.X = x;
                            current.Y = originalY;
                            found = true;
                            break;
                        }
                    }
                }

                // Segunda prioridad: bajar y buscar desde la izquierda hacia la derecha.
                if (!found)
                {
                    for (double y = Math.Max(margin, originalY + step);
                        y + current.Height <= bottomLimit + 0.0001 && !found; y += step)
                    {
                        for (double x = margin; x + current.Width <= rightLimit + 0.0001; x += step)
                        {
                            if (IsValidReflowPosition(current, placed, x, y,
                                margin, gap, rightLimit, bottomLimit))
                            {
                                current.X = x;
                                current.Y = y;
                                found = true;
                                break;
                            }
                        }
                    }
                }

                // Si no hay hueco, se conserva esta foto en su sitio y se continúa
                // con las demás, evitando cancelar el movimiento que hizo el usuario.
                if (!found)
                {
                    current.X = originalX;
                    current.Y = originalY;
                }
                placed.Add(index);
            }

            return true;
        }

        private bool IsValidReflowPosition(PhotoPlacement current, List<int> placed,
            double x, double y, double margin, double gap, double rightLimit, double bottomLimit)
        {
            if (x < margin - 0.0001 || y < margin - 0.0001 ||
                x + current.Width > rightLimit + 0.0001 ||
                y + current.Height > bottomLimit + 0.0001) return false;

            foreach (int placedIndex in placed)
            {
                PhotoPlacement obstacle = _placements[placedIndex];
                bool overlaps = x < obstacle.X + obstacle.Width + gap - 0.0001 &&
                    x + current.Width + gap > obstacle.X + 0.0001 &&
                    y < obstacle.Y + obstacle.Height + gap - 0.0001 &&
                    y + current.Height + gap > obstacle.Y + 0.0001;
                if (overlaps) return false;
            }

            return true;
        }

        private bool CanPlaceAt(int excludedIndex, int page, double x, double y, double width, double height, double gap)
        {
            for (int i = 0; i < _placements.Count; i++)
            {
                if (i == excludedIndex) continue;
                PhotoPlacement other = _placements[i]; if (other.Page != page) continue;
                if (x < other.X + other.Width + gap - 0.0001 && x + width + gap > other.X + 0.0001 && y < other.Y + other.Height + gap - 0.0001 && y + height + gap > other.Y + 0.0001) return false;
            }
            return true;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlacementIndex < 0 || _selectedPlacementIndex >= _placements.Count) return;
            string name = _photos[_placements[_selectedPlacementIndex].PhotoIndex].FileName;
            _placements.RemoveAt(_selectedPlacementIndex); _selectedPlacementIndex = -1;
            _pageCount = _placements.Count == 0 ? 0 : GetHighestPage() + 1;
            if (_pageCount > 0 && _currentPage >= _pageCount) _currentPage = _pageCount - 1;
            if (_pageCount == 0) DrawEmptyPage(); else RenderCurrentPage();
            UpdatePageNavigation(); StatusText.Text = "Se eliminó de la composición: " + name + ". El archivo original sigue importado.";
        }

        private void MoveSelectedToPage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlacementIndex < 0 || _selectedPlacementIndex >= _placements.Count) return;
            int targetPage = MovePageCombo.SelectedIndex;
            PhotoPlacement p = _placements[_selectedPlacementIndex];
            if (targetPage < 0 || targetPage == p.Page) return;
            double x, y, gap = 0, margin = 0;
            ReadNonNegative(GapText.Text, out gap); ReadNonNegative(MarginText.Text, out margin);
            if (targetPage == _pageCount)
            {
                x = margin; y = margin;
                if (x + p.Width > _pageWidthCm - margin || y + p.Height > _pageHeightCm - margin)
                { StatusText.Text = "La fotografía no cabe en una hoja nueva con los márgenes actuales."; return; }
                p.Page = _pageCount; p.X = x; p.Y = y; _pageCount++;
            }
            else
            {
                if (targetPage >= _pageCount || !FindFreePositionOnPage(targetPage, _selectedPlacementIndex, p.Width, p.Height, margin, gap, out x, out y))
                { StatusText.Text = "No hay espacio suficiente en la hoja seleccionada."; return; }
                p.Page = targetPage; p.X = x; p.Y = y;
            }
            _currentPage = p.Page; RenderCurrentPage();
            StatusText.Text = "Fotografía trasladada a la hoja " + (_currentPage + 1) + ".";
        }

        private bool FindFreePositionOnPage(int page, int excludedIndex, double width, double height, double margin, double gap, out double foundX, out double foundY)
        {
            const double step = 0.2;
            for (double y = margin; y + height <= _pageHeightCm - margin + 0.0001; y += step)
                for (double x = margin; x + width <= _pageWidthCm - margin + 0.0001; x += step)
                    if (CanPlaceAt(excludedIndex, page, x, y, width, height, gap)) { foundX = x; foundY = y; return true; }
            foundX = 0; foundY = 0; return false;
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
            PageMediaSize printerSize = SelectedCapability<PageMediaSize>(PaperSizeCombo);
            if (printerSize != null && printerSize.Width.HasValue && printerSize.Height.HasValue)
            {
                width = printerSize.Width.Value * 2.54 / Dpi;
                height = printerSize.Height.Value * 2.54 / Dpi;
            }
            else switch (PaperCombo.SelectedIndex)
            {
                case 1: width = 21.0; height = 29.7; break;
                case 2: width = 21.59; height = 33.0; break;
                default: width = 21.59; height = 27.94; break;
            }
        }

        private static bool TryReadNumber(string text, out double value)
        {
            value = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string normalized = text.Trim().Replace(" ", "");
            if (normalized.Contains(",") && normalized.Contains("."))
            {
                int comma = normalized.LastIndexOf(',');
                int dot = normalized.LastIndexOf('.');
                if (comma > dot) normalized = normalized.Replace(".", "").Replace(',', '.');
                else normalized = normalized.Replace(",", "");
            }
            else normalized = normalized.Replace(',', '.');

            return double.TryParse(normalized, System.Globalization.NumberStyles.AllowLeadingSign |
                System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out value)
                && !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static bool ReadPositive(string text, out double value)
        { return TryReadNumber(text, out value) && value > 0; }

        private static bool ReadNonNegative(string text, out double value)
        { return TryReadNumber(text, out value) && value >= 0; }

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
