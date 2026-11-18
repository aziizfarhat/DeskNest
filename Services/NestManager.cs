using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DeskNest
{
    public sealed partial class NestManager : IDisposable
    {
        private AppData _data = new();
        private readonly List<NestWindow> _windows = new();
        private readonly DispatcherTimer _saveTimer;
        private GlobalHotkey? _hotkey;
        private GlobalHotkey? _searchHotkey;
        private MouseHook? _mouseHook;
        private SettingsWindow? _settings;
        private SearchPaletteWindow? _searchPalette;
        private QuickActionsWindow? _quickActions;
        private GuideOverlay? _guides;
        private DispatcherTimer? _updateTimer;
        private bool _updateBusy;

        // Annuler / rétablir (disposition uniquement : les fichiers ne sont jamais restaurés).
        // Instantanés compacts : uniquement les champs modifies par l'historique,
        // jamais les listes d'éléments (elles ont leurs propres commandes d'annulation).
        private readonly Dictionary<Guid, ZoneValues> _baseline = new();
        private bool _applyingHistory;
        private DelegateUndo? _pendingZoneCmd;
        private Guid _pendingZoneId;
        private DateTime _pendingZoneAt;
        private bool _warnedProtected, _warnedFailure, _warnedHotkey, _hotkeyActive;
        private bool _warnedSearchHotkey, _searchHotkeyActive;
        private string _activeToggleHotkey = "";
        private string _activeSearchHotkey = "";
        private readonly DispatcherTimer _desktopTimer;
        private readonly DispatcherTimer _desktopWake;
        private DesktopWatcher? _desktopWatcher;
        private int _desktopWakeQueued;
        private bool _desktopSyncing;
        private readonly DispatcherTimer _displayTimer;
        private bool _applyingProfileBounds;

        // Trimming à l'inactivité : vérification toutes les 5 s, Action au bout de 60 s
        // sans saisie ou quand toutes les zones sont masquées, avec un délai minimal
        // entre deux passages pour éviter de répéter le GC en boucle.
        private readonly DispatcherTimer _trimTimer;
        private DateTime _lastTrimAt = DateTime.MinValue;
        private const int IdleSeconds = 60;
        private const int TrimCooldownSeconds = 60;

        // Démarrage : les éléments du Bureau public sont traités après l'affichage des
        // zones. Une seule question est posée pour tous les éléments concernés.
        private readonly List<PendingPublicDesktopMove> _pendingPublicDesktopMoves = new();
        private bool _deferPublicDesktopPrompts;
        private bool? _publicDesktopRestoreAnswer;
        private sealed record PendingPublicDesktopMove(string Source, string Destination, bool IsRestore);

        public bool Hidden { get; private set; }
        public AppSettings Settings => _data.Settings;
        public IList<NestData> Nests => _data.Nests;
        public IEnumerable<NestWindow> Windows => _windows;

        public NestManager()
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Storage.Save(_data); };
            // Le suivi est désormais événementiel (DesktopWatcher) : le timer n'est qu'un filet de sécurité.
            _desktopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _desktopTimer.Tick += (_, _) => DesktopTick();
            // Regroupe les rafales d'événements (Win+D masque/restaure des dizaines de fenêtres à la suite).
            _desktopWake = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _desktopWake.Tick += (_, _) => { _desktopWake.Stop(); DesktopTick(); };
            // Regroupe les rafales de WM_DISPLAYCHANGE (brancher un écran peut en envoyer plusieurs).
            _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _displayTimer.Tick += (_, _) => RefreshDisplayProfile();
            _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _trimTimer.Tick += (_, _) => MaybeTrimMemory();
        }

        public void Start()
        {
#if DEBUG
            LogMemory("démarrage");
#endif
            _data = Storage.Load();
            Loc.SetLanguage(_data.Settings.Language);
            if (Storage.Issue == Storage.LoadIssue.RestoredBackup)
                Warn(Loc.F("msg.layoutRestored", Storage.RestoredFrom ?? ""));
            else if (Storage.Issue == Storage.LoadIssue.Lost)
                Warn(Loc.T("msg.layoutLost"));

            // Si les écrans ont changé pendant que l'application était fermée, on
            // restaure (ou on adapte) avant de créer les fenêtres.
            ApplyDisplayProfileFromCurrentMonitors();

            if (_data.Nests.Count == 0)
            {
                var first = NewNestData(Loc.T("zone.apps"));
                first.Left = 80; first.Top = 80;
                _data.Nests.Add(first);
                _data.ZonesCleared = false;
            }

            // Reprend les fichiers gérés avant d'afficher les Nests.
            RestoreManagedItemsOnStartup();

            // Le constructeur recadre dans l'écran virtuel : lors d'une restauration de
            // profil on réapplique les valeurs exactes juste après, sans les laisser dériver.
            _applyingProfileBounds = true;
            try
            {
                foreach (var f in _data.Nests.ToList())
                    OpenWindow(f);
            }
            finally { _applyingProfileBounds = false; }
            InitBaselines();
#if DEBUG
            LogMemory("zones affichées");
#endif

            try
            {
                _hotkey = new GlobalHotkey();
                _hotkey.Pressed += ToggleHidden;
            }
            catch (Exception ex) { Diag.Log("raccourci : " + ex.Message); }

            try
            {
                _searchHotkey = new GlobalHotkey();
                _searchHotkey.Pressed += ShowSearchPalette;
            }
            catch (Exception ex) { Diag.Log("raccourci recherche : " + ex.Message); }

            ApplySettings();
            try
            {
                _desktopWatcher = new DesktopWatcher(QueueDesktopTick);
                _desktopWatcher.Start();
            }
            catch (Exception ex) { Diag.Log("suivi bureau : " + ex.Message); }
            SubscribeDisplayChanges();
            _desktopTimer.Start();
            _trimTimer.Start();

            // L'invite du Bureau public attend que les zones soient visibles : au
            // démarrage, aucune boîte de dialogue ne doit retarder leur affichage.
            Application.Current?.Dispatcher.BeginInvoke(
                new Action(ProcessPendingPublicDesktopMoves), DispatcherPriority.ApplicationIdle);
        }

        private NestData NewNestData(string title) => new NestData
        {
            Title = title,
            Color = Settings.DefaultColor,
            Opacity = Settings.DefaultOpacity,
            IconSize = Settings.DefaultIconSize
        };

        private bool ShouldShow(NestWindow w) => Settings.Enabled && !Hidden && w.Data.Enabled;

        private void OpenWindow(NestData d)
        {
            // Une seule fenêtre par zone : un import ou un JSON contenant des doublons
            // ne doit pas superposer deux fenêtres identiques.
            if (_windows.Any(w => w.Data == d || w.Data.Id == d.Id)) return;

            var w = new NestWindow(this, d);
            if (_applyingProfileBounds) w.SetBounds(d.Left, d.Top, d.Width, d.Height);
            _windows.Add(w);
            // Filet de sécurité : si la fenêtre est réellement fermée (fermeture
            // système, plantage de l'affichage...), on ne garde pas de référence morte.
            w.Closed += (_, _) => _windows.Remove(w);
            if (ShouldShow(w)) w.Show();
        }

        public void RequestSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private static void Warn(string message) =>
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                MessageBox.Show(message, "DeskNest", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, Loc.MsgOptions)));

        // Libère les pages inutilisées une fois que l'utilisateur n'interagit plus.
        private void MaybeTrimMemory()
        {
            // Un trim pendant un glisser, un redimensionnement, une édition ou une
            // synchronisation du bureau ferait disparaître des pages encore en usage.
            if (_desktopSyncing || _desktopWake.IsEnabled || Volatile.Read(ref _desktopWakeQueued) != 0) return;
            if (_windows.Any(w => w.IsBusy)) return;
            if (DateTime.UtcNow - _lastTrimAt < TimeSpan.FromSeconds(TrimCooldownSeconds)) return;

            bool allHidden = Hidden || !Settings.Enabled ||
                             (_windows.Count > 0 && _windows.All(w => !w.IsVisible));
            bool idle = ProcessMemory.IdleTime() >= TimeSpan.FromSeconds(IdleSeconds);
            if (!allHidden && !idle) return;

            _lastTrimAt = DateTime.UtcNow;
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
            ProcessMemory.TrimWorkingSet();
#if DEBUG
            LogMemory("après trim");
#endif
        }

#if DEBUG
        // Sonde de diagnostic uniquement compilée en Debug : aucune interface, aucun
        // coût en Release. « privé » est la mémoire privée engagée (private bytes),
        // complément du working set demandé dans la mesure.
        private static void LogMemory(string stage)
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                const long mo = 1024 * 1024;
                Diag.Log($"MEM [{stage}] workingSet={p.WorkingSet64 / mo} Mo, privé={p.PrivateMemorySize64 / mo} Mo, gc={GC.GetTotalMemory(false) / mo} Mo");
            }
            catch { }
        }
#endif

        public void Dispose()
        {
            _saveTimer.Stop();
            _desktopWake.Stop();
            _desktopTimer.Stop();
            _displayTimer.Stop();
            UnsubscribeDisplayChanges();
            _trimTimer.Stop();
            _updateTimer?.Stop();
            _desktopWatcher?.Dispose();
            _desktopWatcher = null;

            // Remet les fichiers sur le Bureau sans perdre leurs références.
            RestoreManagedItemsOnShutdown();

            // Sauvegarde les associations pour le prochain démarrage.
            Storage.Save(_data);
            Storage.Flush(); // l'écriture se fait en arrière-plan : on l'attend avant de quitter

            _mouseHook?.Dispose();
            _hotkey?.Dispose();
            _searchHotkey?.Dispose();
            _guides?.Dispose();
            _quickActions?.CloseWindow();
            _settings?.Close();

            foreach (var w in _windows.ToList())
                w.CloseForReal();
            // La fermeture des fenêtres a vidé les notes ; on réessaie celles dont
            // l'écriture avait échoué plus tôt (le texte est resté en mémoire).
            NotesStore.RetryPending();
        }
    }
}
