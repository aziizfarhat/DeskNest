using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace DeskNest
{
    public class NestData
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "Nouvelle zone";
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public double Width { get; set; } = 320;
        public double Height { get; set; } = 240;
        public bool Locked { get; set; }
        public bool TitleVisible { get; set; } = true;
        public bool RolledUp { get; set; }
        public string Color { get; set; } = "#1E1E2E";
        public double Opacity { get; set; } = 0.75;
        public int IconSize { get; set; } = 40;
        public string TitleFont { get; set; } = "";
        public int TitleFontSize { get; set; } = 13;
        public string ItemFont { get; set; } = "";
        public int ItemFontSize { get; set; } = 11;
        public bool Enabled { get; set; } = true;      // zone visible ou non (désactivable depuis les paramètres)
        public bool HoverExpand { get; set; } = true;   // participe au « développer au survol » (si l'option globale est active)

        [JsonIgnore] public string Label => Enabled ? Title : Title + " " + Loc.T("z.hidden");

        // Zone classique : chemins des éléments, dans l'ordre choisi (références, jamais déplacés).
        public List<string> Items { get; set; } = new();

        // Folder Portal : dossier affiché dans la zone + ordre personnalisé par dossier (noms de fichiers).
        public string? PortalPath { get; set; }
        public Dictionary<string, List<string>> PortalOrder { get; set; } = new();
    }

    public class AppData
    {
        public int Version { get; set; } = 2;
        public List<NestData> Nests { get; set; } = new();
        // Éléments du bureau que l'application a masqués (attribut "caché") après un glisser dans une zone.
        public List<string> HiddenByApp { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
        // Éléments du bureau rangés dans le dossier géré (chemin actuel + emplacement d'origine pour les remettre).
        public List<MovedItem> Moved { get; set; } = new();
    }

    public class MovedItem
    {
        public string StorePath { get; set; } = "";
        public string OriginalPath { get; set; } = "";
    }

    public class AppSettings
    {
        public bool Enabled { get; set; } = true;                 // DeskNest actif (zones affichées)
        public bool HotkeyEnabled { get; set; } = true;           // Ctrl + Alt + F
        public bool DesktopDoubleClickEnabled { get; set; } = true;
        public bool HideDesktopIcons { get; set; } = true;
        public int DesktopMode { get; set; } = 0;                 // 0 = déplacer dans le dossier géré, 1 = seulement cacher
        public bool AnimateRoll { get; set; } = true;             // animation du Roll
        public int RollAnimationMs { get; set; } = 180;
        public bool HoverExpand { get; set; } = true;            // développer au survol (zones réduites)
        public int HoverExpandDelayMs { get; set; } = 250;
        public int HoverCollapseDelayMs { get; set; } = 500;
        public string DefaultColor { get; set; } = "#1E1E2E";
        public double DefaultOpacity { get; set; } = 0.75;
        public int DefaultIconSize { get; set; } = 40;
        public int CornerRadius { get; set; } = 10;
        public bool ShowBorder { get; set; } = true;
        public string Language { get; set; } = "en";             // langue de l'interface (anglais par défaut)
    }

    public static class Palette
    {
        public static readonly (string Name, string Hex)[] Presets =
        {
            ("c.slate", "#1E1E2E"), ("c.blue", "#1F3A5F"), ("c.green", "#1F4D3A"),
            ("c.purple", "#3D2B56"), ("c.red", "#5A2230"), ("c.gray", "#2B2B2B"),
            ("c.black", "#111111"), ("c.brown", "#4A3426"), ("c.teal", "#1F4D4D")
        };
    }

    public class ItemVM : INotifyPropertyChanged
    {
        public string FullPath { get; }
        public string Name { get; }
        public int IconSize { get; }

        // L'icône est chargée en arrière-plan : la zone s'affiche immédiatement, même sur un gros dossier.
        private ImageSource? _icon;
        public ImageSource? Icon
        {
            get => _icon;
            private set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }
        public bool IsUp { get; }
        public bool IsFolder { get; }
        public bool CanRemove { get; }
        public double CellWidth => IconSize + 34;

        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        // Renommage sur place (clic sur le nom, F2)
        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            }
        }

        private string _editText = "";
        public string EditText
        {
            get => _editText;
            set
            {
                if (_editText == value) return;
                _editText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditText)));
            }
        }

        public ItemVM(string path, int iconSize, bool isPortal = false, bool isUp = false)
        {
            FullPath = path;
            IconSize = iconSize;
            IsUp = isUp;
            IsFolder = isUp || Directory.Exists(path);
            CanRemove = !isPortal && !isUp;
            if (isUp) Name = "..";
            else
            {
                var n = Directory.Exists(path)
                    ? System.IO.Path.GetFileName(path.TrimEnd('\\'))
                    : System.IO.Path.GetFileNameWithoutExtension(path);
                Name = string.IsNullOrEmpty(n) ? path : n;
            }

            Icon = IconHelper.GetCached(FullPath);
            if (Icon == null) LoadIconAsync();
        }

        private async void LoadIconAsync()
        {
            try
            {
                var icon = await IconHelper.GetIconAsync(FullPath);
                if (icon != null && _icon == null) Icon = icon;
            }
            catch { }
        }
    }
}
