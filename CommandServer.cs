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
            int timeoutMilliseconds = 6000)
        {
            if (command != "--new-zone" && command != "--new-portal")
                return false;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            Exception? lastError = null;

            // L'instance principale peut être encore en train de démarrer :
            // on retente la connexion au lieu de perdre silencieusement la commande.
            while (DateTime.UtcNow < deadline)
            {
                NamedPipeClientStream? pipe = null;

                try
                {
                    pipe = new NamedPipeClientStream(
                        ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                    var remaining = deadline - DateTime.UtcNow;
                    using var connect = new CancellationTokenSource(
                        TimeSpan.FromMilliseconds(Math.Min(1000, Math.Max(1, remaining.TotalMilliseconds))));

                    await pipe.ConnectAsync(connect.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    await DelayBeforeRetryAsync(deadline).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex)
                {
                    pipe?.Dispose();
                    lastError = ex;
                    await DelayBeforeRetryAsync(deadline).ConfigureAwait(false);
                    continue;
                }

                if (pipe == null)
                {
                    await DelayBeforeRetryAsync(deadline).ConfigureAwait(false);
                    continue;
                }

                // Une fois la connexion établie, on n'envoie la commande qu'une fois :
                // la rejouer après un délai pourrait créer une zone en double.
                try
                {
                    using (pipe)
                    {
                        using var reader = new StreamReader(
                            pipe, Encoding.UTF8, false, 1024, true);

                        using var writer = new StreamWriter(
                            pipe, new UTF8Encoding(false), 1024, true)
                        {
                            AutoFlush = true
                        };

                        var remaining = deadline - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero) return false;
                        using var io = new CancellationTokenSource(remaining);

                        await writer.WriteLineAsync(command)
                            .WaitAsync(io.Token)
                            .ConfigureAwait(false);

                        var response = await reader.ReadLineAsync()
                            .WaitAsync(io.Token)
                            .ConfigureAwait(false);

                        return response == "OK";
                    }
                }
                catch (Exception ex)
                {
                    Diag.Log("Envoi de commande : " + ex.Message);
                    return false;
                }
            }

            Diag.Log("Envoi de commande : instance principale indisponible" +
                     (lastError == null ? "" : " (" + lastError.Message + ")"));
            return false;
        }

        private static async Task DelayBeforeRetryAsync(DateTime deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return;

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Min(200, remaining.TotalMilliseconds)))
                    .ConfigureAwait(false);
            }
            catch { }
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
