using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SpeedPhotos.App
{
    public partial class App : Application
    {
        private const string MutexName = @"Local\SpeedPhotos.SingleInstance";
        private const string PipeName = "SpeedPhotos.SingleInstance.Pipe";
        private Mutex _instanceMutex;
        private bool _ownsMutex;
        private volatile bool _isShuttingDown;
        private MainWindow _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool createdNew;
            _instanceMutex = new Mutex(true, MutexName, out createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                bool delivered = SendFilesToRunningInstance(e.Args);
                if (!delivered)
                {
                    MessageBox.Show("SpeedPhotos ya se está ejecutando, pero no fue posible enviarle las fotografías. " +
                        "Inténtalo nuevamente.", "SpeedPhotos", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                Shutdown();
                return;
            }

            _mainWindow = new MainWindow();
            StartPipeServer();
            MainWindow = _mainWindow;
            _mainWindow.Show();

            if (e.Args != null && e.Args.Length > 0)
                _mainWindow.ImportExternalFiles(e.Args);
        }

        private void StartPipeServer()
        {
            Task.Run(() =>
            {
                while (!_isShuttingDown)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName,
                            PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            server.WaitForConnection();
                            using (var reader = new BinaryReader(server))
                            {
                                int count = reader.ReadInt32();
                                if (count < 0 || count > 500)
                                    throw new InvalidDataException("Cantidad de archivos no válida.");

                                var files = new string[count];
                                for (int i = 0; i < count; i++)
                                    files[i] = reader.ReadString();

                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (_mainWindow == null) return;
                                    if (files.Length > 0)
                                        _mainWindow.ImportExternalFiles(files);
                                    if (!_mainWindow.IsVisible)
                                        _mainWindow.Show();
                                    if (_mainWindow.WindowState == WindowState.Minimized)
                                        _mainWindow.WindowState = WindowState.Normal;
                                    _mainWindow.Activate();
                                }));
                            }
                        }
                    }
                    catch (IOException)
                    {
                        if (_isShuttingDown) break;
                    }
                    catch (ObjectDisposedException)
                    {
                        if (_isShuttingDown) break;
                    }
                    catch
                    {
                        if (_isShuttingDown) break;
                    }
                }
            });
        }

        private static bool SendFilesToRunningInstance(string[] files)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(200);
                        using (var writer = new BinaryWriter(client))
                        {
                            int count = files == null ? 0 : files.Length;
                            writer.Write(count);
                            for (int i = 0; i < count; i++)
                                writer.Write(files[i] ?? String.Empty);
                            writer.Flush();
                        }
                    }
                    return true;
                }
                catch (TimeoutException)
                {
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }

            return false;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isShuttingDown = true;
            if (_ownsMutex && _instanceMutex != null)
            {
                try { _instanceMutex.ReleaseMutex(); }
                catch { }
            }
            if (_instanceMutex != null)
                _instanceMutex.Dispose();

            base.OnExit(e);
        }
    }
}
