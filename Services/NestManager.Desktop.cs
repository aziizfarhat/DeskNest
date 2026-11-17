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
    public sealed partial class NestManager
    {
        // ---------- Icônes du bureau ----------
        // Appelé quand un élément est déposé dans une zone. Retourne le chemin à enregistrer dans la zone.
        // Mode 0 : l'élément est déplacé dans le dossier géré (il disparaît réellement du bureau).
        // Mode 1 (ou repli) : l'élément reste en place et reçoit les attributs « caché + système ».
        public string TakeFromDesktop(string path)
        {
            if (!Settings.HideDesktopIcons || !DesktopHelper.IsOnDesktop(path)) return path;
            var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));

            if (Settings.DesktopMode == 0)
            {
                bool isDir = Directory.Exists(path);
                var dst = DesktopStore.UniquePath(DesktopStore.Dir, name, isDir);
                bool ok = DesktopStore.TryMove(path, dst, out bool denied);
                if (!ok && denied && DesktopHelper.IsOnPublicDesktop(path))
                {
                    if (_deferPublicDesktopPrompts)
                    {
                        // Au démarrage, la question est posée après l'affichage
                        // des zones (voir ProcessPendingPublicDesktopMoves).
                        _pendingPublicDesktopMoves.Add(new PendingPublicDesktopMove(path, dst, false));
                        return path;
                    }

                    var r = MessageBox.Show(Loc.F("msg.publicDesktop", name), "DeskNest", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
                    if (r == MessageBoxResult.Yes) ok = DesktopStore.TryMoveElevated(path, dst);
                }
                if (ok)
                {
                    _data.Moved.Add(new MovedItem { StorePath = dst, OriginalPath = path });
                    // Écriture immédiate : si DeskNest s'arrête maintenant, le fichier
                    // déjà déplacé dans le stockage reste associé à son emplacement d'origine.
                    Storage.SaveNow(_data);
                    return dst;
                }
            }

            if (HideByAttributes(path)) return path;
            if (!_warnedFailure)
            {
                _warnedFailure = true;
                Warn(Loc.F("msg.cannotRemove", name));
            }
            return path;
        }

        private bool HideByAttributes(string path)
        {
            if (_data.HiddenByApp.Any(p => Same(p, path))) return true;
            if (DesktopHelper.IsHiddenOrSystem(path)) return true; // déjà caché par l'utilisateur : on n'y touche pas
            if (!DesktopHelper.SetHidden(path, true)) return false;

            _data.HiddenByApp.Add(path);
            RequestSave();
            if (!_warnedProtected && DesktopHelper.ShowsProtectedFiles())
            {
                _warnedProtected = true;
                Warn(Loc.T("msg.protectedWarn"));
            }
            return true;
        }

        // Ré-adopte les éléments référencés par une zone visible mais encore
        // présents sur le bureau. C'est le cas après une réinstallation : la
        // désinstallation les a remis sur le bureau et les zones pointent vers
        // leur nouvel emplacement. Sans cela, le même fichier apparaît à la
        // fois sur le bureau et dans la zone.
        private bool AdoptDesktopItemsInEnabledZones(NestData? only = null)
        {
            if (!Settings.Enabled || !Settings.HideDesktopIcons) return false;

            bool changed = false;
            foreach (var f in _data.Nests.ToList())
            {
                if (only != null && !ReferenceEquals(f, only)) continue;
                if (!f.Enabled || !string.IsNullOrEmpty(f.PortalPath)) continue;

                for (int i = 0; i < f.Items.Count; i++)
                {
                    var p = f.Items[i];
                    if (_data.Moved.Any(m => Same(m.StorePath, p))) continue;
                    if (_data.HiddenByApp.Any(h => Same(h, p))) continue;
                    if (!File.Exists(p) && !Directory.Exists(p)) continue;
                    if (!DesktopHelper.IsOnDesktop(p)) continue;

                    var adopted = TakeFromDesktop(p);

                    // Un même fichier peut être référencé par plusieurs zones :
                    // toutes doivent pointer vers le nouvel emplacement.
                    bool moved = RebaseReferences(_data.Nests, p, adopted);

                    // Ne compter un changement que si l'élément est réellement
                    // géré à présent (déplacé dans le stockage ou masqué).
                    if (moved ||
                        _data.Moved.Any(m => Same(m.StorePath, adopted)) ||
                        _data.HiddenByApp.Any(h => Same(h, adopted)))
                        changed = true;
                }
            }

            return changed;
        }

        // Remet un élément rangé à son emplacement d'origine (ou à l'emplacement
        // préféré, s'il est encore libre). Retourne le nouveau chemin, ou null en cas d'échec.
        private string? MoveBack(MovedItem m, string? preferredPath = null)
        {
            if (!File.Exists(m.StorePath) && !Directory.Exists(m.StorePath)) return m.OriginalPath; // plus rien à remettre
            var target = string.IsNullOrWhiteSpace(preferredPath) ? m.OriginalPath : preferredPath!;
            var destDir = System.IO.Path.GetDirectoryName(target.TrimEnd('\\'));
            if (string.IsNullOrEmpty(destDir)) destDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            bool isDir = Directory.Exists(m.StorePath);
            var dst = DesktopStore.UniquePath(destDir, System.IO.Path.GetFileName(target.TrimEnd('\\')), isDir);
            bool ok = DesktopStore.TryMove(m.StorePath, dst, out bool denied);
            if (!ok && denied && DesktopHelper.IsOnPublicDesktop(target))
            {
                var r = MessageBox.Show(Loc.T("msg.publicDesktopBack"), "DeskNest", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, Loc.MsgOptions);
                if (r == MessageBoxResult.Yes) ok = DesktopStore.TryMoveElevated(m.StorePath, dst);
            }
            return ok ? dst : null;
        }

        // Appelé quand un élément n'est plus dans aucune zone : il retourne sur le bureau.
        // `preferredDesktopPath` permet de retrouver exactement son ancien emplacement.
        // Retourne le chemin actuel (sur le bureau si le déplacement a réussi).
        public string? RestoreOnDesktop(string path, string? preferredDesktopPath = null)
        {
            if (_data.Nests.Any(f => f.Items.Any(p => Same(p, path)))) return path; // encore référencé ailleurs

            int mi = _data.Moved.FindIndex(m => Same(m.StorePath, path));
            if (mi >= 0)
            {
                var restored = MoveBack(_data.Moved[mi], preferredDesktopPath);
                if (restored != null)
                {
                    _data.Moved.RemoveAt(mi);
                    // Le fichier a quitté le stockage : l'enregistrement doit disparaître
                    // tout de suite, sinon un arrêt brutal le ferait reprendre au démarrage.
                    Storage.SaveNow(_data);
                    return restored;
                }
                Warn(Loc.F("msg.cannotRestore", path));
                RequestSave();
                return path;
            }

            int i = _data.HiddenByApp.FindIndex(p => Same(p, path));
            if (i < 0) return path;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                _data.HiddenByApp.RemoveAt(i); // plus rien à restaurer
            }
            else if (DesktopHelper.SetHidden(path, false)) _data.HiddenByApp.RemoveAt(i);
            else Warn(Loc.F("msg.cannotRestore", path));
            RequestSave();
            return path;
        }

        // Restauration sans interface, utilisée par le désinstalleur : remet les
        // éléments rangés sur le bureau et met à jour les références des zones.
        public static int RestoreAllForUninstall()
        {
            var data = Storage.Load();
            int n = 0;

            foreach (var m in data.Moved.ToList())
            {
                // Plus rien dans le stockage : le fichier a déjà été restauré
                // (fermeture normale de l'application). Les zones doivent
                // pointer vers cet emplacement avant qu'on oublie la trace.
                if (!File.Exists(m.StorePath) && !Directory.Exists(m.StorePath))
                {
                    if (!string.IsNullOrEmpty(m.OriginalPath) &&
                        (File.Exists(m.OriginalPath) || Directory.Exists(m.OriginalPath)))
                        RebaseReferences(data.Nests, m.StorePath, m.OriginalPath);

                    data.Moved.Remove(m);
                    continue;
                }
                if (string.IsNullOrEmpty(m.OriginalPath))
                    continue;

                var destDir = System.IO.Path.GetDirectoryName(m.OriginalPath.TrimEnd('\\'));
                if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
                    destDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(destDir)) continue;

                bool isDir = Directory.Exists(m.StorePath);
                var name = System.IO.Path.GetFileName(m.OriginalPath.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name))
                    name = System.IO.Path.GetFileName(m.StorePath.TrimEnd('\\'));

                var dst = DesktopStore.UniquePath(destDir, name, isDir);
                if (!DesktopStore.TryMove(m.StorePath, dst, out _))
                {
                    Diag.Log($"désinstallation : impossible de restaurer {m.StorePath}");
                    continue;
                }

                foreach (var f in data.Nests)
                    for (int i = 0; i < f.Items.Count; i++)
                        if (Same(f.Items[i], m.StorePath)) f.Items[i] = dst;

                data.Moved.Remove(m);
                n++;
            }

            foreach (var p in data.HiddenByApp.ToList())
            {
                if (!File.Exists(p) && !Directory.Exists(p))
                {
                    data.HiddenByApp.Remove(p);
                    continue;
                }
                if (DesktopHelper.SetHidden(p, false))
                    data.HiddenByApp.Remove(p);
            }

            Storage.Save(data);
            Storage.Flush(); // le processus de désinstallation se termine juste après

            // Le dossier de stockage ne doit pas rester après désinstallation,
            // mais on ne le supprime que s'il est vide : un fichier qu'on n'a pas
            // pu remettre sur le bureau ne doit jamais être effacé.
            try
            {
                var store = DesktopStore.Dir;
                if (Directory.Exists(store) && !Directory.EnumerateFileSystemEntries(store).Any())
                    Directory.Delete(store);
            }
            catch { }

            return n;
        }

        // Remet toutes les icônes sur le bureau (elles restent aussi dans leurs zones).
        public int RestoreAllHidden()
        {
            int n = 0;
            // On ne perd pas la trace des éléments qui n'ont pas pu être restaurés.
            var stillHidden = new List<string>();
            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (DesktopHelper.SetHidden(p, false)) n++;
                else if (File.Exists(p) || Directory.Exists(p)) stillHidden.Add(p);
                // chemin disparu : on abandonne la trace (il n'y a plus rien à restaurer)
            }
            _data.HiddenByApp = stillHidden;

            foreach (var m in _data.Moved.ToList())
            {
                var restored = MoveBack(m);
                if (restored == null) continue;
                // Les zones qui référençaient l'élément pointent vers le bureau.
                RebaseReferences(_data.Nests, m.StorePath, restored);
                _data.Moved.Remove(m);
                n++;
            }
            foreach (var w in _windows) w.ReloadItems();
            // Commande explicite de l'utilisateur : l'état doit être sur disque
            // avant de rendre la main (pas d'attente du minuteur de 500 ms).
            Storage.SaveNow(_data);
            return n;
        }

        // ---------- Zones et « Afficher le bureau » (Win+D, bouton en bas à droite) ----------
        // Les zones doivent rester visibles : on les restaure si le système les réduit ou les masque,
        // et on les remonte au-dessus du bureau quand celui-ci est affiché par-dessus les fenêtres.
        private void QueueDesktopTick()
        {
            // Un callback WinEvent peut arriver sur le thread qui a installé le hook : on repasse
            // par le dispatcher et on fusionne les demandes en attente.
            if (Interlocked.Exchange(ref _desktopWakeQueued, 1) == 1) return;
            var app = Application.Current;
            if (app == null) { Interlocked.Exchange(ref _desktopWakeQueued, 0); return; }

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _desktopWakeQueued, 0);
                if (!Settings.Enabled || Hidden) return;
                if (!_desktopWake.IsEnabled) _desktopWake.Start();
            }), DispatcherPriority.Background);
        }

        private void DesktopTick()
        {
            if (!Settings.Enabled || Hidden) return;
            _desktopSyncing = true;
            try
            {
                var host = DesktopHelper.FindDesktopHost();
                bool desktopForeground = DesktopHelper.IsDesktopForeground(host);
                foreach (var w in _windows)
                    if (ShouldShow(w)) w.EnsureDesktopVisibility(host, desktopForeground);
            }
            finally { _desktopSyncing = false; }
        }

        // ---------- Glisser un élément hors d'une zone (vers le bureau, l'Explorateur...) ----------
        // Les éléments masqués par attribut sont démasqués pendant le glisser (sinon ils arriveraient cachés).
        public List<string> UnhideForDrag(List<string> paths)
        {
            var list = new List<string>();
            foreach (var p in paths)
                if (_data.HiddenByApp.Any(h => Same(h, p)) && DesktopHelper.SetHidden(p, false)) list.Add(p);
            return list;
        }

        public void RehideAfterDrag(List<string> paths)
        {
            foreach (var p in paths)
                if (File.Exists(p) || Directory.Exists(p)) DesktopHelper.SetHidden(p, true);
        }

        // L'élément a été déplacé hors de DeskNest par l'utilisateur : on ne garde plus de trace.
        public void ForgetStored(string path)
        {
            _data.Moved.RemoveAll(m => Same(m.StorePath, path));
            _data.HiddenByApp.RemoveAll(p => Same(p, path));
            RequestSave();
        }

        // ---------- Quick-Hide ----------
        public void ToggleHidden()
        {
            if (!Settings.Enabled) return;
            Hidden = !Hidden;
            ApplyVisibility();
        }

        private void OnDesktopDoubleClick(int x, int y)
        {
            if (!Settings.Enabled || !Settings.DesktopDoubleClickEnabled) return;
            if (!DesktopHelper.IsDesktopBackground(x, y)) return;
            Task.Run(() =>
            {
                if (DesktopHelper.IsIconAt(x, y)) return; // double-clic sur une icône : on laisse Windows l'ouvrir
                Application.Current?.Dispatcher.BeginInvoke(new Action(ToggleHidden));
            });
        }

    }
}
