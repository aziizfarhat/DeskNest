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
        private CommandServer? _commandServer;
        private DesktopContextMenu? _desktopContextMenu;
        private bool _fatalReported;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Processus eleve lance pour deplacer un element du bureau public.
            // Il ne doit pas demarrer une nouvelle instance de l'application.
            if (DesktopStore.TryRunElevatedMove(e.Args))
            {
                Shutdown(Environment.ExitCode);
                return;
            }

            _mutex = new Mutex(true, "DeskNest.SingleInstance", out _ownsMutex);

            // Une autre instance de DeskNest fonctionne deja.
            if (!_ownsMutex)
            {
                if (e.Args.Length == 1 &&
                    (e.Args[0] == "--new-zone" ||
                     e.Args[0] == "--new-portal"))
                {
                    bool sent = await CommandServer.SendAsync(e.Args[0]);

                    if (!sent)
                        Diag.Log("Impossible de transmettre la commande a l'instance principale.");
                }

                Shutdown();
                return;
            }

            _manager = new NestManager();
            _manager.Start();

            _tray = new TrayIcon(_manager);

            // Demarrage du serveur de commandes.
            _commandServer = new CommandServer(Dispatcher, HandleCommand);

            try
            {
                await _commandServer.StartAsync();
                Diag.Log("Serveur de commandes demarre.");

                // Enregistrement du menu contextuel du bureau.
                // Il utilise le serveur de commandes pour communiquer
                // avec cette instance de DeskNest.
                _desktopContextMenu = new DesktopContextMenu();
            }
            catch (Exception ex)
            {
                Diag.Log("Demarrage du serveur de commandes impossible : " + ex);

                _commandServer.Dispose();
                _commandServer = null;
            }

            // Si DeskNest vient d'etre lance par une ancienne entree
            // du menu contextuel (apres un plantage, par exemple),
            // on execute directement la commande demandee.
            if (e.Args.Length == 1 &&
                (e.Args[0] == "--new-zone" ||
                 e.Args[0] == "--new-portal"))
            {
                HandleCommand(e.Args[0]);
            }
        }

        private void HandleCommand(string command)
        {
            switch (command)
            {
                case "--new-zone":
                    _manager?.CreateNest();
                    break;

                case "--new-portal":
                    _manager?.CreatePortal();
                    break;

                default:
                    Diag.Log("Commande inconnue : " + command);
                    break;
            }
        }

        // Une erreur d'interface ne doit pas fermer les zones sans laisser de trace.
        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            Diag.Log("FATAL (UI) : " + e.Exception);

            if (!_fatalReported)
            {
                _fatalReported = true;

                try
                {
                    MessageBox.Show(
                        Loc.F("msg.unexpected", e.Exception.Message),
                        "DeskNest",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error,
                        MessageBoxResult.OK,
                        Loc.MsgOptions);
                }
                catch { }
            }

            e.Handled = true;
        }

        private static void OnUnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
            => Diag.Log("FATAL (thread) : " + e.ExceptionObject);

        private static void OnUnobservedTaskException(
            object? sender,
            UnobservedTaskExceptionEventArgs e)
        {
            Diag.Log("FATAL (tache) : " + e.Exception);
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Supprimer d'abord le menu du registre.
            _desktopContextMenu?.Dispose();
            _desktopContextMenu = null;

            // Arrêter ensuite le serveur de commandes.
            _commandServer?.Dispose();
            _commandServer = null;

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