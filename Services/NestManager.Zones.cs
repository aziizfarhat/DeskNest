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
        // ---------- Zones ----------
        public IList<NestTemplate> Templates => _data.Templates;

        public void CreateNest() => AddNest(NewNestData(""));

        // Création depuis les actions rapides : la zone apparaît dans l'écran du
        // curseur, sans recouvrir les zones déjà visibles, puis son titre est éditable.
        public void CreateNestAtCursor(string type)
        {
            var d = NewNestData("");
            d.Type = NestTypes.IsKnown(type) ? type : NestTypes.Files;
            if (d.Type == NestTypes.Todo) d.Height = 300;
            PlaceAtCursor(d);
            FinishQuickCreate(d);
        }

        public void CreatePortalAtCursor()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("dlg.chooseFolder") };
            if (dlg.ShowDialog() != true) return; // annulation : aucune zone créée
            var name = System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\'));
            var d = NewNestData(string.IsNullOrEmpty(name) ? dlg.FolderName : name);
            d.Type = NestTypes.Files;
            d.PortalPath = dlg.FolderName;
            PlaceAtCursor(d);
            FinishQuickCreate(d);
        }

        private void PlaceAtCursor(NestData d)
        {
            var occupied = _windows.Where(w => w.IsVisible)
                .Select(w => new Rect(w.Left, w.Top,
                    w.ActualWidth > 0 ? w.ActualWidth : w.Data.Width,
                    Math.Max(w.Data.Height, w.ActualHeight)))
                .ToList();
            var spot = CursorPlacement.FindFreeSpot(CursorPlacement.Get(), d.Width, d.Height, occupied);
            d.Left = spot.X;
            d.Top = spot.Y;
        }

        private void FinishQuickCreate(NestData d)
        {
            AddNest(d, place: false);
            _windows.FirstOrDefault(w => w.Data == d)?.BeginTitleEdit();
        }

        public void CreatePortal()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("dlg.chooseFolder") };
            if (dlg.ShowDialog() != true) return;
            var name = System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\'));
            var d = NewNestData(string.IsNullOrEmpty(name) ? dlg.FolderName : name);
            d.PortalPath = dlg.FolderName;
            AddNest(d);
        }

        // Duplique une zone complète : apparence, taille et références (les fichiers ne sont pas copiés).
        public void DuplicateNest(NestData source)
        {
            var d = new NestData
            {
                Title = Loc.F("zone.copyName", source.Title),
                Color = source.Color,
                Opacity = source.Opacity,
                BackgroundMode = source.BackgroundMode,
                GradientStart = source.GradientStart,
                GradientEnd = source.GradientEnd,
                GradientAngle = source.GradientAngle,
                BackgroundImage = source.BackgroundImage,
                IconSize = source.IconSize,
                ListView = source.ListView,
                Width = source.Width,
                Height = source.Height,
                TitleVisible = source.TitleVisible,
                TitleFont = source.TitleFont,
                TitleFontSize = source.TitleFontSize,
                ItemFont = source.ItemFont,
                ItemFontSize = source.ItemFontSize,
                HoverExpand = source.HoverExpand,
                PortalPath = source.PortalPath,
                Items = new List<string>(source.Items)
            };
            foreach (var kv in source.PortalOrder)
                d.PortalOrder[kv.Key] = new List<string>(kv.Value);
            d.Left = source.Left + 30;
            d.Top = source.Top + 30;
            AddNest(d, false);
        }

        // Enregistre l'apparence d'une zone comme modèle. Un nom existant met le modèle à jour.
        public void SaveTemplate(NestData source, string name)
        {
            name = name.Trim();
            if (name.Length == 0) return;

            var t = _data.Templates.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (t == null)
            {
                t = new NestTemplate();
                _data.Templates.Add(t);
            }
            t.Name = name;
            t.Color = source.Color;
            t.Opacity = source.Opacity;
            t.BackgroundMode = source.BackgroundMode;
            t.GradientStart = source.GradientStart;
            t.GradientEnd = source.GradientEnd;
            t.GradientAngle = source.GradientAngle;
            t.BackgroundImage = source.BackgroundImage;
            t.IconSize = source.IconSize;
            t.ListView = source.ListView;
            t.Width = source.Width;
            t.Height = source.Height;
            t.TitleVisible = source.TitleVisible;
            t.TitleFont = source.TitleFont;
            t.TitleFontSize = source.TitleFontSize;
            t.ItemFont = source.ItemFont;
            t.ItemFontSize = source.ItemFontSize;
            RequestSave();
        }

        public void CreateNestFromTemplate(NestTemplate t)
        {
            var d = new NestData
            {
                Title = t.Name,
                Color = t.Color,
                Opacity = t.Opacity,
                BackgroundMode = t.BackgroundMode,
                GradientStart = t.GradientStart,
                GradientEnd = t.GradientEnd,
                GradientAngle = t.GradientAngle,
                BackgroundImage = t.BackgroundImage,
                IconSize = t.IconSize,
                ListView = t.ListView,
                Width = t.Width,
                Height = t.Height,
                TitleVisible = t.TitleVisible,
                TitleFont = t.TitleFont,
                TitleFontSize = t.TitleFontSize,
                ItemFont = t.ItemFont,
                ItemFontSize = t.ItemFontSize
            };
            AddNest(d);
        }

        public void DeleteTemplate(NestTemplate t)
        {
            _data.Templates.Remove(t);
            RequestSave();
        }

        // Applique l'apparence et la taille d'un modèle à une zone existante (sans toucher aux éléments).
        public void ApplyTemplate(NestData target, NestTemplate t)
        {
            EditNest(target, Loc.T("undo.applyTemplate"), () =>
            {
                target.Color = t.Color;
                target.Opacity = t.Opacity;
                target.BackgroundMode = t.BackgroundMode;
                target.GradientStart = t.GradientStart;
                target.GradientEnd = t.GradientEnd;
                target.GradientAngle = t.GradientAngle;
                target.BackgroundImage = t.BackgroundImage;
                target.IconSize = t.IconSize;
                target.ListView = t.ListView;
                target.Width = t.Width;
                target.Height = t.Height;
                target.TitleVisible = t.TitleVisible;
                target.TitleFont = t.TitleFont;
                target.TitleFontSize = t.TitleFontSize;
                target.ItemFont = t.ItemFont;
                target.ItemFontSize = t.ItemFontSize;
                if (!target.TitleVisible) target.RolledUp = false;
            });

            var w = _windows.FirstOrDefault(x => x.Data == target);
            if (w != null)
            {
                w.ApplyExternalChange();
                w.SetBounds(target.Left, target.Top, target.Width, target.Height);
            }
        }

        public void ShowTemplates()
        {
            ShowSettings();
            _settings?.SelectTemplates();
        }

        private void AddNest(NestData d, bool place = true)
        {
            int n = _data.Nests.Count;
            if (string.IsNullOrEmpty(d.Title)) d.Title = Loc.F("zone.default", n + 1);
            if (place)
            {
                d.Left = 80 + 30 * (n % 10);
                d.Top = 80 + 30 * (n % 10);
            }
            int index = _data.Nests.Count;
            _data.Nests.Add(d);
            _data.ZonesCleared = false;
            Hidden = false;
            Settings.Enabled = true; // créer une zone la rend forcément visible
            OpenWindow(d);
            ApplyVisibility();
            _baseline[d.Id] = ZoneValues.From(d);
            PushUndo(new DelegateUndo(Loc.T("undo.newZone"),
                () => RemoveNestSilent(d),
                () => ReopenNestAt(d, index)));
            RequestSave();
        }

        // Retire une zone sans toucher aux fichiers (annulation d'une création).
        private void RemoveNestSilent(NestData d)
        {
            NotesStore.Delete(d.Id); // une note supprimée avec sa zone ne doit pas rester sur le disque
            _data.Nests.Remove(d);
            _data.ZonesCleared = _data.Nests.Count == 0;
            _baseline.Remove(d.Id);
            var w = _windows.FirstOrDefault(x => x.Data == d);
            if (w != null)
            {
                _windows.Remove(w);
                w.CloseForReal();
            }
            ApplyVisibility();
            RequestSave();
        }

        // Remet une zone supprimée (ou annulée) à sa place, avec ses références.
        private void ReopenNestAt(NestData d, int index)
        {
            if (_data.Nests.Contains(d)) return;
            int at = Math.Clamp(index, 0, _data.Nests.Count);
            _data.Nests.Insert(at, d);
            _data.ZonesCleared = false;
            OpenWindow(d);
            _baseline[d.Id] = ZoneValues.From(d);
            _applyingHistory = true;
            try { AdoptDesktopItemsInEnabledZones(d); } // les fichiers avaient été remis sur le bureau
            finally { _applyingHistory = false; }
            ApplyVisibility();
            RequestSave();
        }

        public void Remove(NestWindow w)
        {
            var items = w.Data.Items.ToList();
            var data = w.Data;
            int index = _data.Nests.IndexOf(data);
            _data.Nests.Remove(data);
            _data.ZonesCleared = _data.Nests.Count == 0;
            _baseline.Remove(data.Id);
            _windows.Remove(w);
            w.CloseForReal();
            NotesStore.Delete(data.Id);
            foreach (var p in items) RestoreOnDesktop(p);
            PushUndo(new DelegateUndo(Loc.T("undo.deleteZone"),
                () => ReopenNestAt(data, index),
                () => RemoveNestSilent(data)));
            RequestSave();
        }

        public void RemoveNest(NestData d)
        {
            var w = _windows.FirstOrDefault(x => x.Data == d);
            if (w != null) Remove(w);
        }

        // Déplace plusieurs éléments d'une zone vers une autre (à partir d'un Folder Portal : copie de références).
        public void MoveItems(Guid sourceId, List<string> paths, NestWindow target, int index)
        {
            if (sourceId == target.Data.Id) return;
            var src = _windows.FirstOrDefault(w => w.Data.Id == sourceId);
            if (src == null) return;
            bool fromPortal = !string.IsNullOrEmpty(src.Data.PortalPath);
            int at = index;
            History.BeginBatch();
            try
            {
                foreach (var p in paths)
                {
                    if (!fromPortal && !src.RemoveItem(p, false)) continue;
                    target.AddItem(p, at);
                    if (at >= 0) at++;
                }
            }
            finally { History.EndBatch(Loc.T("undo.moveItems")); }
        }

    }
}
