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
        // ---------- Annuler / rétablir ----------
        public UndoStack History { get; } = new();

        public void Undo()
        {
            _pendingZoneCmd = null;
            if (!History.Undo()) return;
            ApplyVisibility();
            RequestSave();
        }

        public void Redo()
        {
            _pendingZoneCmd = null;
            if (!History.Redo()) return;
            ApplyVisibility();
            RequestSave();
        }

        private static NestData CloneNest(NestData d) => new()
        {
            Id = d.Id,
            Title = d.Title,
            Left = d.Left,
            Top = d.Top,
            Width = d.Width,
            Height = d.Height,
            Locked = d.Locked,
            TitleVisible = d.TitleVisible,
            RolledUp = d.RolledUp,
            Color = d.Color,
            Opacity = d.Opacity,
            BackgroundMode = d.BackgroundMode,
            GradientStart = d.GradientStart,
            GradientEnd = d.GradientEnd,
            GradientAngle = d.GradientAngle,
            BackgroundImage = d.BackgroundImage,
            IconSize = d.IconSize,
            ListView = d.ListView,
            TitleFont = d.TitleFont,
            TitleFontSize = d.TitleFontSize,
            ItemFont = d.ItemFont,
            ItemFontSize = d.ItemFontSize,
            Enabled = d.Enabled,
            HoverExpand = d.HoverExpand,
            Items = new List<string>(d.Items),
            PortalPath = d.PortalPath,
            PortalOrder = d.PortalOrder.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value))
        };

        private static bool SameLayout(NestData a, NestData b)
        {
            if (a.Title != b.Title || a.Locked != b.Locked || a.TitleVisible != b.TitleVisible ||
                a.RolledUp != b.RolledUp || a.Color != b.Color || a.Opacity != b.Opacity ||
                a.BackgroundMode != b.BackgroundMode || a.GradientStart != b.GradientStart ||
                a.GradientEnd != b.GradientEnd || Math.Abs(a.GradientAngle - b.GradientAngle) > 0.01 ||
                a.BackgroundImage != b.BackgroundImage ||
                a.IconSize != b.IconSize || a.ListView != b.ListView ||
                a.TitleFont != b.TitleFont || a.TitleFontSize != b.TitleFontSize ||
                a.ItemFont != b.ItemFont || a.ItemFontSize != b.ItemFontSize ||
                a.Enabled != b.Enabled || a.HoverExpand != b.HoverExpand ||
                a.PortalPath != b.PortalPath ||
                Math.Abs(a.Left - b.Left) > 0.01 || Math.Abs(a.Top - b.Top) > 0.01 ||
                Math.Abs(a.Width - b.Width) > 0.01 || Math.Abs(a.Height - b.Height) > 0.01)
                return false;
            // Les références d'éléments ont leurs propres commandes d'annulation : une
            // modification d'apparence ne doit jamais les inclure.
            return true;
        }

        private void InitBaselines()
        {
            _baseline.Clear();
            foreach (var d in _data.Nests) _baseline[d.Id] = CloneNest(d);
        }

        private void PushUndo(IUndoable action)
        {
            _pendingZoneCmd = null;
            History.Push(action);
        }

        // Regroupe les modifications rapprochées d'une même zone (curseur d'opacité, saisie du titre…).
        private void PushZoneDiff(Guid id, string label, NestData before, NestData after)
        {
            if (_applyingHistory) return;
            var now = DateTime.UtcNow;
            if (!History.IsBatching && _pendingZoneCmd != null && _pendingZoneId == id &&
                (now - _pendingZoneAt).TotalMilliseconds < 900)
            {
                _pendingZoneCmd.ReplaceRedo(() => ApplyZoneStateById(id, after));
                _pendingZoneAt = now;
                return;
            }
            var command = new DelegateUndo(label,
                () => ApplyZoneStateById(id, before),
                () => ApplyZoneStateById(id, after));
            if (!History.IsBatching)
            {
                _pendingZoneCmd = command;
                _pendingZoneId = id;
                _pendingZoneAt = now;
            }
            History.Push(command);
        }

        private void ApplyZoneStateById(Guid id, NestData state)
        {
            var live = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (live == null) return;
            _applyingHistory = true;
            try
            {
                bool boundsChanged =
                    Math.Abs(live.Left - state.Left) > 0.01 || Math.Abs(live.Top - state.Top) > 0.01 ||
                    Math.Abs(live.Width - state.Width) > 0.01 || Math.Abs(live.Height - state.Height) > 0.01;

                live.Title = state.Title;
                live.Left = state.Left;
                live.Top = state.Top;
                live.Width = state.Width;
                live.Height = state.Height;
                live.Locked = state.Locked;
                live.TitleVisible = state.TitleVisible;
                live.RolledUp = state.RolledUp;
                live.Color = state.Color;
                live.Opacity = state.Opacity;
                live.BackgroundMode = state.BackgroundMode;
                live.GradientStart = state.GradientStart;
                live.GradientEnd = state.GradientEnd;
                live.GradientAngle = state.GradientAngle;
                live.BackgroundImage = state.BackgroundImage;
                live.IconSize = state.IconSize;
                live.ListView = state.ListView;
                live.TitleFont = state.TitleFont;
                live.TitleFontSize = state.TitleFontSize;
                live.ItemFont = state.ItemFont;
                live.ItemFontSize = state.ItemFontSize;
                live.Enabled = state.Enabled;
                live.HoverExpand = state.HoverExpand;
                live.PortalPath = state.PortalPath;

                var w = _windows.FirstOrDefault(x => x.Data == live);
                if (w != null)
                {
                    w.ApplyExternalChange();
                    if (boundsChanged) w.SetBounds(live.Left, live.Top, live.Width, live.Height);
                    w.ReloadItems();
                }
                _baseline[id] = CloneNest(live);
            }
            finally { _applyingHistory = false; }
        }

        private void ApplyBoundsById(Guid id, double left, double top, double width, double height)
        {
            var live = _data.Nests.FirstOrDefault(x => x.Id == id);
            if (live == null) return;
            _applyingHistory = true;
            try
            {
                live.Left = left;
                live.Top = top;
                live.Width = width;
                live.Height = height;
                _windows.FirstOrDefault(x => x.Data == live)?.SetBounds(left, top, width, height);
                _baseline[id] = CloneNest(live);
            }
            finally { _applyingHistory = false; }
        }

        // À appeler après toute modification de zone qui ne passe pas par ZoneChanged (menu contextuel).
        public void EditNest(NestData d, string label, Action change)
        {
            if (_applyingHistory) { change(); return; }
            var before = _baseline.TryGetValue(d.Id, out var b) ? b : CloneNest(d);
            change();
            var after = CloneNest(d);
            if (!SameLayout(before, after)) PushZoneDiff(d.Id, label, before, after);
            _baseline[d.Id] = after;
            RequestSave();
        }

        public void RecordBounds(NestData d, double left, double top, double width, double height, string label)
        {
            if (_applyingHistory) return;
            if (Math.Abs(d.Left - left) < 0.5 && Math.Abs(d.Top - top) < 0.5 &&
                Math.Abs(d.Width - width) < 0.5 && Math.Abs(d.Height - height) < 0.5) return;
            var id = d.Id;
            double nl = d.Left, nt = d.Top, nw = d.Width, nh = d.Height;
            PushUndo(new DelegateUndo(label,
                () => ApplyBoundsById(id, left, top, width, height),
                () => ApplyBoundsById(id, nl, nt, nw, nh)));
            _baseline[id] = CloneNest(d);
        }

        // ---------- Instantanés ----------
        public string? CreateSnapshot()
        {
            Storage.Save(_data);
            return Storage.CreateSnapshot();
        }

        public bool RestoreSnapshot(string path)
        {
            var d = Storage.LoadFrom(path);
            if (d == null) return false;
            CreateSnapshot(); // filet de sécurité : l'état actuel est conservé

            foreach (var p in _data.HiddenByApp.ToList()) DesktopHelper.SetHidden(p, false);
            var oldMoved = _data.Moved.ToList();

            foreach (var w in _windows.ToList()) w.CloseForReal();
            _windows.Clear();
            _data = d;
            _data.Settings ??= new AppSettings();
            _data.ZonesCleared = _data.Nests.Count == 0;
            // On garde la trace des éléments rangés par l'état précédent pour pouvoir les remettre un jour.
            foreach (var m in oldMoved)
                if (!_data.Moved.Any(x => Same(x.StorePath, m.StorePath))) _data.Moved.Add(m);

            foreach (var f in _data.Nests.ToList()) OpenWindow(f);
            foreach (var p in _data.HiddenByApp.ToList())
                if (File.Exists(p) || Directory.Exists(p)) DesktopHelper.SetHidden(p, true);
            History.Clear();
            _pendingZoneCmd = null;
            InitBaselines();
            ApplySettings();
            RequestSave();
            return true;
        }

    }
}
