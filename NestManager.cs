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
        private GuideOverlay? _guides;
        private DispatcherTimer? _updateTimer;
        private bool _updateBusy;

        // Annuler / rétablir (disposition uniquement : les fichiers ne sont jamais restaurés).
        private readonly Dictionary<Guid, NestData> _baseline = new();
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
        }

        public void Start()
        {
            _data = Storage.Load();
            Loc.SetLanguage(_data.Settings.Language);
            if (Storage.Issue == Storage.LoadIssue.RestoredBackup)
                Warn(Loc.F("msg.layoutRestored", Storage.RestoredFrom ?? ""));
            else if (Storage.Issue == Storage.LoadIssue.Lost)
                Warn(Loc.T("msg.layoutLost"));

            if (_data.Nests.Count == 0)
            {
                var first = NewNestData(Loc.T("zone.apps"));
                first.Left = 80; first.Top = 80;
                _data.Nests.Add(first);
                _data.ZonesCleared = false;
            }

            // Reprend les fichiers gérés avant d'afficher les Nests.
            RestoreManagedItemsOnStartup();

            foreach (var f in _data.Nests.ToList())
                OpenWindow(f);
            InitBaselines();

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
            _desktopTimer.Start();

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

        public void Dispose()
        {
            _saveTimer.Stop();
            _desktopWake.Stop();
            _desktopTimer.Stop();
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
            _settings?.Close();

            foreach (var w in _windows.ToList())
                w.CloseForReal();
        }
    }
}
