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
        // ---------- Dépôt dans un Folder Portal : les fichiers sont réellement mis dans le dossier ----------
        private void OnPathMoved(string oldPath, string newPath)
        {
            foreach (var f in _data.Nests)
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], oldPath)) f.Items[i] = newPath;
            ForgetStored(oldPath);
        }

        private static void ShellMove(string src, string dst, bool isDir)
        {
            var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
            var cancel = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;
            if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(src, dst, ui, cancel);
            else Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(src, dst, ui, cancel);
        }

        private static void ShellCopy(string src, string dst, bool isDir)
        {
            var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
            var cancel = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;
            if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(src, dst, ui, cancel);
            else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(src, dst, ui, cancel);
        }

        // Déplace (ou copie) les éléments dans le dossier. Utilise les opérations de l'Explorateur
        // (progression, confirmations, autorisation administrateur si nécessaire).
        public void DropIntoFolder(List<string> paths, string folder, bool copy, Guid? sourceNest)
        {
            if (!Directory.Exists(folder)) { Warn(Loc.T("msg.folderNotFound")); return; }
            var src = sourceNest.HasValue ? _windows.FirstOrDefault(w => w.Data.Id == sourceNest.Value) : null;
            bool srcIsReferenceNest = src != null && string.IsNullOrEmpty(src.Data.PortalPath);
            var dest = folder.TrimEnd('\\');

            foreach (var path in paths)
            {
                bool isDir = Directory.Exists(path);
                if (!isDir && !File.Exists(path)) continue;
                var clean = path.TrimEnd('\\');
                var name = System.IO.Path.GetFileName(clean);
                var parent = System.IO.Path.GetDirectoryName(clean);
                if (parent != null && Same(parent.TrimEnd('\\'), dest)) continue; // déjà dans ce dossier
                if (isDir && (Same(clean, dest) || (dest + "\\").StartsWith(clean + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    Warn(Loc.T("msg.folderIntoItself"));
                    continue;
                }

                var dst = System.IO.Path.Combine(folder, name);
                bool wasHidden = _data.HiddenByApp.Any(h => Same(h, path));
                if (wasHidden) DesktopHelper.SetHidden(path, false);
                try
                {
                    if (copy) ShellCopy(path, dst, isDir); else ShellMove(path, dst, isDir);
                }
                catch (Exception ex)
                {
                    Warn(Loc.F("msg.opFailed", name, ex.Message));
                }

                bool sourceGone = !File.Exists(path) && !Directory.Exists(path);
                bool destExists = File.Exists(dst) || Directory.Exists(dst);
                if (!copy && sourceGone && destExists)
                {
                    OnPathMoved(path, dst);
                    if (srcIsReferenceNest) src!.RemoveItem(dst, false); // l'élément quitte la zone d'origine
                }
                else if (wasHidden && !sourceGone) DesktopHelper.SetHidden(path, true); // rien n'a bougé : on remet le masquage
            }
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
        }

        // ---------- Renommer / supprimer de vrais fichiers ----------
        private static void MoveAny(string a, string b)
        {
            if (Directory.Exists(a)) Directory.Move(a, b); else File.Move(a, b);
        }

        // Noms réservés par Windows (y compris avec une extension, ex. « CON.txt »).
        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // Un nom saisi par l'utilisateur doit être accepté par Windows sans
        // être normalisé (point/espaces finaux, nom de périphérique réservé...).
        public static bool IsValidItemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (name.EndsWith(' ') || name.EndsWith('.')) return false;

            var stem = Path.GetFileNameWithoutExtension(name).TrimEnd(' ');
            return !ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
        }

        public bool RenameItem(string path, string newName)
        {
            var clean = path.TrimEnd('\\');
            var dir = System.IO.Path.GetDirectoryName(clean);
            if (string.IsNullOrEmpty(dir)) return false;
            if (!IsValidItemName(newName))
            {
                Warn(Loc.T("msg.renameInvalid"));
                return false;
            }
            var dst = System.IO.Path.Combine(dir, newName);
            bool caseOnly = Same(dst, clean);
            if (!caseOnly && (File.Exists(dst) || Directory.Exists(dst)))
            {
                Warn(Loc.T("msg.renameExists"));
                return false;
            }
            try
            {
                if (caseOnly)
                {
                    var tmp = clean + "." + Guid.NewGuid().ToString("N") + ".tmp"; // changement de casse seulement
                    MoveAny(clean, tmp);
                    MoveAny(tmp, dst);
                }
                else MoveAny(clean, dst);
            }
            catch (Exception ex)
            {
                Warn(Loc.F("msg.renameFailed", ex.Message));
                return false;
            }

            RenameTracked(clean, dst);
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
            return true;
        }

        private void RenameTracked(string oldPath, string newPath)
        {
            var dir = System.IO.Path.GetDirectoryName(oldPath) ?? "";
            var oldName = System.IO.Path.GetFileName(oldPath);
            var newName = System.IO.Path.GetFileName(newPath);
            var oldClean = oldPath.TrimEnd('\\');
            string Rebase(string path)
            {
                var clean = path.TrimEnd('\\');
                if (Same(clean, oldClean)) return newPath;
                if (clean.StartsWith(oldClean + "\\", StringComparison.OrdinalIgnoreCase))
                    return newPath + clean.Substring(oldClean.Length); // sous-dossier du dossier renommé
                return path;
            }

            foreach (var f in _data.Nests)
            {
                for (int i = 0; i < f.Items.Count; i++)
                    if (Same(f.Items[i], oldPath)) f.Items[i] = newPath;

                // Le dossier renommé (ou un de ses sous-dossiers) peut être le Folder Portal affiché.
                if (!string.IsNullOrEmpty(f.PortalPath)) f.PortalPath = Rebase(f.PortalPath);

                if (f.PortalOrder.Count == 0) continue;
                var order = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in f.PortalOrder)
                {
                    var folder = Rebase(kv.Key);
                    if (Same(folder.TrimEnd('\\'), dir.TrimEnd('\\'))) // ordre personnalisé du dossier parent
                        for (int i = 0; i < kv.Value.Count; i++)
                            if (Same(kv.Value[i], oldName)) kv.Value[i] = newName;
                    if (order.TryGetValue(folder, out var existing)) existing.AddRange(kv.Value);
                    else order[folder] = kv.Value;
                }
                f.PortalOrder = order; // les clés suivent le dossier renommé
            }
            foreach (var m in _data.Moved)
                if (Same(m.StorePath, oldPath))
                {
                    m.StorePath = newPath;
                    var od = System.IO.Path.GetDirectoryName(m.OriginalPath.TrimEnd('\\')) ?? "";
                    m.OriginalPath = System.IO.Path.Combine(od, newName);
                }
            for (int i = 0; i < _data.HiddenByApp.Count; i++)
                if (Same(_data.HiddenByApp[i], oldPath)) _data.HiddenByApp[i] = newPath;
        }

        // Envoie les éléments à la Corbeille (la confirmation est demandée par la zone).
        public void DeleteItems(List<string> paths)
        {
            foreach (var p in paths)
            {
                bool isDir = Directory.Exists(p);
                if (!isDir && !File.Exists(p)) continue;
                var name = System.IO.Path.GetFileName(p.TrimEnd('\\'));
                try
                {
                    var ui = Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;
                    var bin = Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin;
                    if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, ui, bin);
                    else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, ui, bin);
                }
                catch (Exception ex)
                {
                    Warn(Loc.F("msg.deleteFailed", name, ex.Message));
                    continue;
                }
                if (!File.Exists(p) && !Directory.Exists(p))
                {
                    // Suppression réelle : les références disparaissent sans étape d'annulation.
                    foreach (var w in _windows) w.RemoveItem(p, false, record: false);
                    ForgetStored(p);
                }
            }
            foreach (var w in _windows) w.ReloadItems();
            RequestSave();
        }

    }
}
