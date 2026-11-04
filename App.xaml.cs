using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DeskNest
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private bool _ownsMutex;
        private NestManager? _manager;
        private TrayIcon? _tray;
        private bool _fatalReported;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Processus élevé lancé pour déplacer un élément du bureau public : il ne démarre pas l'application.
            if (DesktopStore.TryRunElevatedMove(e.Args))
            {
                Shutdown(Environment.ExitCode);
                return;
            }

            _mutex = new Mutex(true, "DeskNest.SingleInstance", out _ownsMutex);
            if (!_ownsMutex) { Shutdown(); return; }

            _manager = new NestManager();
            _manager.Start();
            _tray = new TrayIcon(_manager);
        }

        // Une erreur d'interface ne doit pas fermer les zones sans laisser de trace.
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Diag.Log("FATAL (UI) : " + e.Exception);
            if (!_fatalReported)
            {
                _fatalReported = true;
                try
                {
                    MessageBox.Show(Loc.F("msg.unexpected", e.Exception.Message), "DeskNest",
                        MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, Loc.MsgOptions);
                }
                catch { }
            }
            e.Handled = true; // on garde l'application en vie (les zones se rechargeront au prochain démarrage)
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
            => Diag.Log("FATAL (thread) : " + e.ExceptionObject);

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Diag.Log("FATAL (tâche) : " + e.Exception);
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _manager?.Dispose();
            if (_ownsMutex)
            {
                try { _mutex?.ReleaseMutex(); } catch { }
            }
            base.OnExit(e);
        }
    }
}
