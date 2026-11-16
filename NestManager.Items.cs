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
        // ---------- Références d'éléments ----------
        private void RefreshZone(NestData d) =>
            _windows.FirstOrDefault(w => w.Data == d)?.ReloadItems();

        // Retrouve le chemin actuel d'un élément : son emplacement d'origine, une copie
        // déplacée dans le stockage, une autre zone qui le référence, ou le bureau
        // (après un « Del » ou un glisser vers le bureau).
        public string ResolveItemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (File.Exists(path) || Directory.Exists(path)) return path;

            var moved = _data.Moved.FirstOrDefault(m =>
                Same(m.OriginalPath, path) && (File.Exists(m.StorePath) || Directory.Exists(m.StorePath)));
            if (moved != null) return moved.StorePath;

            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) return path;

            foreach (var f in _data.Nests)
                foreach (var p in f.Items)
                    if (string.Equals(Path.GetFileName(p.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase) &&
                        (File.Exists(p) || Directory.Exists(p)))
                        return p;

            return FindDesktopEntry(name) ?? path;
        }

        // Retire une référence et retourne le chemin réellement retiré.
        private string? RemoveItemRef(Guid id, string path)
        {
            var d = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (d == null) return null;
            int i = d.Items.FindIndex(p => Same(p, path));
            if (i < 0) return null;
            var removed = d.Items[i];
            d.Items.RemoveAt(i);
            RefreshZone(d);
            RequestSave();
            return removed;
        }

        // Insère une référence, ré-adopte l'élément s'il est sur le bureau et retourne
        // le chemin finalement référencé (il peut changer après adoption).
        private string? InsertItemRef(Guid id, string path, int index)
        {
            var d = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (d == null) return null;
            if (!d.Items.Any(p => Same(p, path)))
            {
                int at = Math.Clamp(index, 0, d.Items.Count);
                d.Items.Insert(at, path);
            }
            _applyingHistory = true;
            try { AdoptDesktopItemsInEnabledZones(d); } // l'élément avait pu être remis sur le bureau
            finally { _applyingHistory = false; }
            RefreshZone(d);
            RequestSave();

            var exact = d.Items.FirstOrDefault(p => Same(p, path));
            if (exact != null) return exact;
            var name = Path.GetFileName(path.TrimEnd('\\'));
            return d.Items.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p.TrimEnd('\\')), name, StringComparison.OrdinalIgnoreCase)) ?? path;
        }

        // `fromDesktop` : l'élément vient du bureau. Annuler l'ajout le remet sur le bureau.
        // `originalDesktopPath` conserve son emplacement d'origine avant le transfert.
        public void RecordItemAdded(NestData d, string path, int index, bool fromDesktop = false,
            string? originalDesktopPath = null)
        {
            if (_applyingHistory) return;
            var id = d.Id;
            string current = path;
            PushUndo(new DelegateUndo(Loc.T("undo.addItem"),
                () =>
                {
                    var removed = RemoveItemRef(id, current);
                    if (removed == null) return;
                    current = removed;
                    if (fromDesktop) current = RestoreOnDesktop(removed, originalDesktopPath) ?? removed;
                },
                () =>
                {
                    var target = fromDesktop ? ResolveItemPath(current) : current;
                    current = InsertItemRef(id, target, index) ?? target;
                }));
        }

        // `restoreOnDesktop` : le retrait remet l'élément sur le bureau (Del, glisser
        // vers le bureau). Annuler le retrait le ré-adopte dans la zone ; rétablir le
        // retire à nouveau et le remet sur le bureau.
        // `desktopPath` conserve l'emplacement exact occupé sur le bureau au moment
        // du retrait, pour que rétablir le remette au même endroit.
        public void RecordItemRemoved(NestData d, string path, int index, bool restoreOnDesktop = false,
            string? desktopPath = null)
        {
            if (_applyingHistory) return;
            var id = d.Id;
            string current = path;
            PushUndo(new DelegateUndo(Loc.T("undo.removeItem"),
                () =>
                {
                    string target = current;
                    if (restoreOnDesktop)
                    {
                        target = desktopPath != null && (File.Exists(desktopPath) || Directory.Exists(desktopPath))
                            ? desktopPath
                            : ResolveItemPath(desktopPath ?? current);
                    }
                    current = InsertItemRef(id, target, index) ?? target;
                },
                () =>
                {
                    var removed = RemoveItemRef(id, current);
                    if (removed == null) return;
                    current = removed;
                    if (restoreOnDesktop) current = RestoreOnDesktop(removed, desktopPath) ?? removed;
                }));
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Met à jour toutes les références d'une zone quand un élément change
        // d'emplacement (stockage <-> bureau), pour qu'une zone ne pointe
        // jamais vers un chemin qui n'existe plus.
        private static bool RebaseReferences(IEnumerable<NestData> nests, string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || Same(from, to)) return false;

            bool changed = false;
            foreach (var f in nests)
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], from))
                    {
                        f.Items[i] = to;
                        changed = true;
                    }

            return changed;
        }

        // Répare les dispositions abîmées par une ancienne désinstallation :
        // la zone pointe encore vers %USERPROFILE%\DeskNest\Items alors que
        // le fichier a été remis sur le bureau. On retrouve son homologue par
        // son nom, puis la ré-adoption le reprend normalement en charge.
        private bool RepairMissingStoreReferences()
        {
            bool changed = false;
            var storeRoot = DesktopStore.Dir.TrimEnd('\\') + "\\";

            foreach (var f in _data.Nests)
            {
                if (!string.IsNullOrEmpty(f.PortalPath)) continue;

                for (int i = 0; i < f.Items.Count; i++)
                {
                    var p = f.Items[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (File.Exists(p) || Directory.Exists(p)) continue;

                    var full = p.TrimEnd('\\');
                    if (!full.StartsWith(storeRoot, StringComparison.OrdinalIgnoreCase)) continue;

                    // Un enregistrement de déplacement existe : c'est le
                    // démarrage normal qui remettra le fichier dans le
                    // stockage. Ne pas court-circuiter ce mécanisme.
                    if (_data.Moved.Any(m => Same(m.StorePath, p))) continue;

                    var name = Path.GetFileName(full);
                    if (string.IsNullOrEmpty(name)) continue;

                    var found = FindDesktopEntry(name);
                    if (found == null) continue;

                    f.Items[i] = found;
                    changed = true;
                }
            }

            return changed;
        }

        private static string? FindDesktopEntry(string name)
        {
            foreach (var dir in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        // Vérifie qu'un chemin appartient à un Nest classique.
        // Les Folder Portals sont volontairement exclus.
        private bool IsReferencedByNormalNest(string path)
        {
            return _data.Nests.Any(f =>
                string.IsNullOrEmpty(f.PortalPath) &&
                f.Items.Any(p => Same(p, path)));
        }

        // Vérifie que l'élément est réellement affiché : DeskNest doit être actif
        // et la zone qui le référence doit être visible.
        private bool IsReferencedByEnabledNormalNest(string path)
        {
            return Settings.Enabled && _data.Nests.Any(f =>
                f.Enabled &&
                string.IsNullOrEmpty(f.PortalPath) &&
                f.Items.Any(p => Same(p, path)));
        }

        // Remet sur le bureau les éléments qui ne sont plus affichés par une zone
        // active (DeskNest désactivé ou zone masquée) et met à jour les références.
        // Sans cela, les fichiers resteraient invisibles alors qu'aucune zone ne les montre.
        private bool RestoreItemsNoLongerVisible()
        {
            bool changed = false;

            foreach (var m in _data.Moved.ToList())
            {
                if (!IsReferencedByNormalNest(m.StorePath))
                    continue;
                if (IsReferencedByEnabledNormalNest(m.StorePath))
                    continue;

                // Le fichier a pu être supprimé depuis : on garde la trace,
                // mais on ne pointe pas les zones vers un chemin inexistant.
                if (!File.Exists(m.StorePath) && !Directory.Exists(m.StorePath) &&
                    !File.Exists(m.OriginalPath) && !Directory.Exists(m.OriginalPath))
                    continue;

                var restored = MoveBack(m);
                if (restored == null) continue;

                // Les zones qui référençaient l'élément pointent maintenant
                // vers son emplacement restauré, pour rester utilisables.
                foreach (var f in _data.Nests)
                    for (int i = 0; i < f.Items.Count; i++)
                        if (Same(f.Items[i], m.StorePath)) f.Items[i] = restored;

                _data.Moved.Remove(m);
                changed = true;
            }

            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (!IsReferencedByNormalNest(p))
                    continue;
                if (IsReferencedByEnabledNormalNest(p))
                    continue;

                if (DesktopHelper.SetHidden(p, false) || (!File.Exists(p) && !Directory.Exists(p)))
                {
                    _data.HiddenByApp.Remove(p);
                    changed = true;
                }
            }

            return changed;
        }

        // Au démarrage : tout élément présent dans le stockage mais absent de
        // `Moved` et d'aucune zone est un orphelin (sauvegarde perdue, ancienne
        // version, copie manuelle…). On le remet sur le bureau au lieu de le
        // laisser invisible dans le dossier géré.
        private void RestoreOrphanedStoreItems()
        {
            string store;
            try { store = DesktopStore.Dir; }
            catch (Exception ex) { Diag.Log("stockage : dossier inaccessible — " + ex.Message); return; }
            if (!Directory.Exists(store)) return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop))
            {
                Diag.Log("stockage : dossier Bureau introuvable, balayage impossible");
                return;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(store))
                {
                    if (_data.Moved.Any(m => Same(m.StorePath, entry))) continue;
                    if (_data.Nests.Any(f => f.Items.Any(p => Same(p, entry)))) continue;

                    bool isDir = Directory.Exists(entry);
                    var name = Path.GetFileName(entry.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(name)) continue;

                    var dst = DesktopStore.UniquePath(desktop, name, isDir);
                    if (DesktopStore.TryMove(entry, dst, out bool denied))
                        Diag.Log($"stockage : élément orphelin restauré sur le bureau : {entry} -> {dst}");
                    else
                        Diag.Log($"stockage : impossible de restaurer l'élément orphelin {entry}" +
                                 (denied ? " (accès refusé)" : ""));
                }
            }
            catch (Exception ex)
            {
                Diag.Log("stockage : balayage impossible — " + ex.Message);
            }
        }

        // Au démarrage : replace les éléments dans le dossier de stockage
        // et réapplique le masquage des éléments gérés par DeskNest.
        // Les éléments du Bureau public sont mis en attente : la question
        // d'élévation n'est posée qu'après l'affichage des zones.
        private void RestoreManagedItemsOnStartup()
        {
            _pendingPublicDesktopMoves.Clear();
            _deferPublicDesktopPrompts = true;
            try
            {
                RestoreOrphanedStoreItems();
                bool changed = RepairMissingStoreReferences();
                if (RestoreItemsNoLongerVisible()) changed = true;

                foreach (var m in _data.Moved.ToList())
                {
                    if (!IsReferencedByEnabledNormalNest(m.StorePath))
                        continue;

                    // Le fichier est déjà dans le stockage : par sécurité, on
                    // s'assure que les zones pointent bien vers cet emplacement.
                    if (File.Exists(m.StorePath) || Directory.Exists(m.StorePath))
                    {
                        if (RebaseReferences(_data.Nests, m.OriginalPath, m.StorePath))
                            changed = true;
                        continue;
                    }

                    // Le fichier n'existe plus à son emplacement d'origine.
                    if (!File.Exists(m.OriginalPath) && !Directory.Exists(m.OriginalPath))
                        continue;

                    bool ok = DesktopStore.TryMove(m.OriginalPath, m.StorePath, out bool denied);

                    if (!ok && denied && DesktopHelper.IsOnPublicDesktop(m.OriginalPath))
                    {
                        // La décision est prise plus tard, avec les autres éléments.
                        _pendingPublicDesktopMoves.Add(
                            new PendingPublicDesktopMove(m.OriginalPath, m.StorePath, true));
                        continue;
                    }

                    if (!ok)
                    {
                        Diag.Log($"impossible de reprendre l'élément : {m.OriginalPath}");
                        continue;
                    }

                    // Le fichier a quitté le bureau : les zones suivent.
                    if (RebaseReferences(_data.Nests, m.OriginalPath, m.StorePath))
                        changed = true;
                }

                // Réapplique les attributs caché + système aux éléments concernés.
                foreach (var p in _data.HiddenByApp.ToList())
                {
                    if (!IsReferencedByEnabledNormalNest(p))
                        continue;

                    if (File.Exists(p) || Directory.Exists(p))
                        DesktopHelper.SetHidden(p, true);
                }

                // Réinstallation, ou zone réactivée : les fichiers remis sur le
                // bureau doivent être repris en charge par leur zone.
                if (AdoptDesktopItemsInEnabledZones())
                    changed = true;

                if (changed) RequestSave();
            }
            finally
            {
                _deferPublicDesktopPrompts = false;
            }
        }

        // Traite les déplacements du Bureau public mis en attente au démarrage.
        // Une seule question est posée pour tous les éléments ; la réponse vaut
        // pour le reste de cette session.
        private void ProcessPendingPublicDesktopMoves()
        {
            if (_pendingPublicDesktopMoves.Count == 0) return;

            var pending = _pendingPublicDesktopMoves.ToList();
            _pendingPublicDesktopMoves.Clear();

            bool allow;
            if (_publicDesktopRestoreAnswer.HasValue)
            {
                allow = _publicDesktopRestoreAnswer.Value;
            }
            else
            {
                var answer = MessageBox.Show(
                    Loc.F("msg.publicDesktopBatch", pending.Count),
                    "DeskNest",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No,
                    Loc.MsgOptions);
                allow = answer == MessageBoxResult.Yes;
                _publicDesktopRestoreAnswer = allow;
            }

            bool changed = false;
            foreach (var item in pending)
            {
                if (!File.Exists(item.Source) && !Directory.Exists(item.Source))
                    continue;
                if (File.Exists(item.Destination) || Directory.Exists(item.Destination))
                    continue;

                bool moved = allow && DesktopStore.TryMoveElevated(item.Source, item.Destination);
                if (moved)
                {
                    if (!_data.Moved.Any(m => Same(m.StorePath, item.Destination)))
                    {
                        _data.Moved.Add(new MovedItem
                        {
                            StorePath = item.Destination,
                            OriginalPath = item.Source
                        });
                    }
                    RebaseReferences(_data.Nests, item.Source, item.Destination);
                    changed = true;
                }
                else if (!item.IsRestore)
                {
                    // Refus ou échec de l'élévation : on retombe sur le masquage
                    // par attributs, comme avant le démarrage différé.
                    if (HideByAttributes(item.Source)) changed = true;
                }
                else
                {
                    Diag.Log($"impossible de reprendre l'élément : {item.Source}");
                }
            }

            if (!changed) return;

            // Les associations viennent de changer : écriture immédiate pour
            // qu'un arrêt brutal ne perde pas les éléments déplacés.
            Storage.SaveNow(_data);
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
        }

        // À la fermeture : restaure les éléments sur le bureau,
        // mais conserve les associations pour le prochain démarrage.
        private void RestoreManagedItemsOnShutdown()
        {
            foreach (var m in _data.Moved.ToList())
            {
                if (!IsReferencedByNormalNest(m.StorePath))
                    continue;

                var restored = MoveBack(m);

                if (restored != null)
                    m.OriginalPath = restored;
            }

            // Retire le masquage sans supprimer les références sauvegardées.
            foreach (var p in _data.HiddenByApp.ToList())
            {
                if (!IsReferencedByNormalNest(p))
                    continue;

                if (File.Exists(p) || Directory.Exists(p))
                    DesktopHelper.SetHidden(p, false);
            }
        }

    }
}
