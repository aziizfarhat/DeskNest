using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DeskNest
{
    public sealed class CommandServer : IDisposable
    {
        private const string PipeName = "DeskNest.CommandServer";

        private readonly Dispatcher _dispatcher;
        private readonly Action<string> _onCommand;
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? _serverTask;
        private NamedPipeServerStream? _activePipe;
        private readonly object _lock = new();
        private bool _disposed;

        public CommandServer(Dispatcher dispatcher, Action<string> onCommand)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _onCommand = onCommand ?? throw new ArgumentNullException(nameof(onCommand));
        }

        public async Task StartAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CommandServer));

            if (_serverTask != null)
                throw new InvalidOperationException("Le serveur est dÃ©jÃ  dÃ©marrÃ©.");

            _serverTask = Task.Run(() => ListenAsync(_cts.Token));

            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;

                try
                {
                    pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    lock (_lock)
                        _activePipe = pipe;

                    // Le point d'entrÃ©e du pipe est maintenant disponible.
                    _ready.TrySetResult(true);

                    await pipe.WaitForConnectionAsync(cancellationToken)
                        .ConfigureAwait(false);

                    using (pipe)
                    {
                        using var reader = new StreamReader(
                            pipe, Encoding.UTF8, false, 1024, true);

                        using var writer = new StreamWriter(
                            pipe, new UTF8Encoding(false), 1024, true)
                        {
                            AutoFlush = true
                        };

                        var command = await reader.ReadLineAsync()
                            .WaitAsync(cancellationToken)
                            .ConfigureAwait(false);

                        if (command != "--new-zone" &&
                            command != "--new-portal")
                        {
                            await writer.WriteLineAsync("ERROR")
                                .ConfigureAwait(false);
                            continue;
                        }

                        if (cancellationToken.IsCancellationRequested ||
                            _dispatcher.HasShutdownStarted ||
                            _dispatcher.HasShutdownFinished)
                        {
                            await writer.WriteLineAsync("ERROR")
                                .ConfigureAwait(false);
                            continue;
                        }

                        // On programme l'action sur le thread WPF sans
                        // attendre une Ã©ventuelle boÃ®te de dialogue.
                        _ = _dispatcher.BeginInvoke(
                            new Action(() => _onCommand(command)));

                        // OK signifie que la commande a Ã©tÃ© mise en file.
                        await writer.WriteLineAsync("OK")
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    Diag.Log("Serveur de commandes : pipe fermÃ©.");
                }
                catch (Exception ex)
                {
                    Diag.Log("Serveur de commandes : " + ex);

                    if (!_ready.Task.IsCompleted)
                    {
                        _ready.TrySetException(ex);
                        break;
                    }

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await Task.Delay(250, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_activePipe, pipe))
                            _activePipe = null;
                    }

                    pipe?.Dispose();
                }
            }

            if (!_ready.Task.IsCompleted)
                _ready.TrySetCanceled(cancellationToken);
        }

        public static async Task<bool> SendAsync(
            string command,
            int timeoutMilliseconds = 3000)
        {
            if (command != "--new-zone" && command != "--new-portal")
                return false;

            try
            {
                using var timeout =
                    new CancellationTokenSource(timeoutMilliseconds);

                using var pipe = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

                using var reader = new StreamReader(
                    pipe, Encoding.UTF8, false, 1024, true);

                using var writer = new StreamWriter(
                    pipe, new UTF8Encoding(false), 1024, true)
                {
                    AutoFlush = true
                };

                await writer.WriteLineAsync(command)
                    .WaitAsync(timeout.Token)
                    .ConfigureAwait(false);

                var response = await reader.ReadLineAsync()
                    .WaitAsync(timeout.Token)
                    .ConfigureAwait(false);

                return response == "OK";
            }
            catch (Exception ex)
            {
                Diag.Log("Envoi de commande : " + ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();

            lock (_lock)
            {
                try { _activePipe?.Dispose(); } catch { }
                _activePipe = null;
            }

            try { _serverTask?.Wait(1000); } catch { }

            _cts.Dispose();
        }
    }
}
