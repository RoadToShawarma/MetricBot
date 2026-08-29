using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace MetricBot
{
    public partial class App : WpfApplication
    {
        private const string MutexName = "MetricBot_SingleInstance_Mutex";
        private const string PipeName  = "MetricBot_SingleInstance_Pipe";

        private Mutex? _mutex;
        private CancellationTokenSource? _pipeCts;

        protected override void OnStartup(WpfStartupEventArgs e)
        {
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out bool isFirstInstance);

            if (!isFirstInstance)
            {
                NotifyFirstInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);

            _pipeCts = new CancellationTokenSource();
            _ = ListenForSecondInstancesAsync(_pipeCts.Token);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(WpfExitEventArgs e)
        {
            try
            {
                _pipeCts?.Cancel();
                _pipeCts?.Dispose();
            }
            catch { }

            try
            {
                _mutex?.ReleaseMutex();
            }
            catch { }

            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void NotifyFirstInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: PipeName,
                    direction: PipeDirection.Out);

                client.Connect(timeout: 1000);

                using var writer = new StreamWriter(client, Encoding.UTF8)
                {
                    AutoFlush = true
                };

                writer.WriteLine("SHOW");
            }
            catch
            {
                // Если первый экземпляр ещё не успел поднять канал — просто молча закрываем второй.
            }
        }

        private async Task ListenForSecondInstancesAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName: PipeName,
                        direction: PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Message,
                        options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var command = await reader.ReadLineAsync();

                    if (string.Equals(command, "SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (this.MainWindow is MetricBot.MainWindow window)
                                window.BringToFront();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Канал не критичен: при ошибке продолжаем слушать следующий запуск.
                }
            }
        }
    }
}
