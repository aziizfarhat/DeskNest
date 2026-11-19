using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace DeskNest
{
    // Types de zones. La valeur par défaut "Files" garantit qu'un ancien layout
    // (sans champ Type) continue à charger exactement comme avant.
    public static class NestTypes
    {
        public const string Files = "Files";
        public const string Notes = "Notes";
        public const string Todo = "Todo";

        public static bool IsKnown(string? type) => type is Files or Notes or Todo;
    }

    // Tâche d'une zone To-do. Stockée dans layout.json (contrairement aux notes).
    public class TodoTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

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
        // Fond de la zone : 0 = couleur unie, 1 = dégradé, 2 = image, 3 = acrylique (flou du papier peint).
        public int BackgroundMode { get; set; }
        public string GradientStart { get; set; } = "#1E1E2E";
        public string GradientEnd { get; set; } = "#4A3426";
        public double GradientAngle { get; set; } = 45;
        public string BackgroundImage { get; set; } = "";
        public int IconSize { get; set; } = 40;
        public bool ListView { get; set; }        // false = grille, true = liste (propre à chaque zone)
        public string TitleFont { get; set; } = "";
        public int TitleFontSize { get; set; } = 13;
        public string ItemFont { get; set; } = "";
        public int ItemFontSize { get; set; } = 11;
        public bool Enabled { get; set; } = true;      // zone visible ou non (désactivable depuis les paramètres)
        public bool HoverExpand { get; set; } = true;   // participe au « développer au survol » (si l'option globale est active)
        // "Files" (par défaut, y compris les portails), "Notes" ou "Todo".
        public string Type { get; set; } = NestTypes.Files;

        // Tri d'affichage, propre à cette zone. Les anciens layouts sans ces
        // champs gardent le tri manuel et leur ordre existant. Ni l'un ni l'autre
        // ne fait partie des profils d'affichage (géométrie seule).
        public string SortBy { get; set; } = NestSort.Manual;
        public bool SortDescending { get; set; }

        [JsonIgnore] public string Label => Enabled ? Title : Title + " " + Loc.T("z.hidden");

        // Une note fraîchement créée s'ouvre en édition ; cet état n'est pas persisté.
        [JsonIgnore] public bool StartEditing { get; set; }

        // Zone classique : chemins des éléments, dans l'ordre choisi (références, jamais déplacés).
        public List<string> Items { get; set; } = new();

        // Tâches d'une zone To-do (ordre manuscrit conservé dans layout.json).
        public List<TodoTask> Tasks { get; set; } = new();

        // Folder Portal : dossier affiché dans la zone + ordre personnalisé par dossier (noms de fichiers).
        public string? PortalPath { get; set; }
        public Dictionary<string, List<string>> PortalOrder { get; set; } = new();
    }

    public class AppData
    {
        public int Version { get; set; } = 3;
        public List<NestData> Nests { get; set; } = new();
        // Dispositions enregistrées par configuration d'écrans. Les champs Left/Top/Width/Height
        // des NestData restent les valeurs vivantes du profil actif.
        public List<DisplayProfile> Profiles { get; set; } = new();
        public string ActiveProfileKey { get; set; } = "";
        // Métadonnées des configurations vues (hors des profils, qui restent géométrie seule).
        public Dictionary<string, List<MonitorLayout>> Displays { get; set; } = new();
        // Éléments du bureau que l'application a masqués (attribut "caché") après un glisser dans une zone.
        public List<string> HiddenByApp { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
        // Éléments du bureau rangés dans le dossier géré (chemin actuel + emplacement d'origine pour les remettre).
        public List<MovedItem> Moved { get; set; } = new();
        // Modèles de zones réutilisables (apparence et taille, sans les éléments).
        public List<NestTemplate> Templates { get; set; } = new();
        // Vrai uniquement si l'utilisateur a volontairement supprimé la dernière zone.
        // Sans ce marqueur, un layout.json vide est considéré comme perdu et restauré
        // depuis la sauvegarde la plus récente (voir Storage.Load).
        public bool ZonesCleared { get; set; }
    }

    // Configuration d'un écran, en pixels physiques (le facteur DPI est stocké à part).
    public class MonitorLayout
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int WorkX { get; set; }
        public int WorkY { get; set; }
        public int WorkWidth { get; set; }
        public int WorkHeight { get; set; }
        public double DpiScale { get; set; } = 1;
        public bool IsPrimary { get; set; }
    }

    // Profil de disposition pour une configuration d'écrans donnée.
    // Un profil ne contient QUE de la géométrie : jamais d'éléments, de titres ou de couleurs.
    public class DisplayProfile
    {
        public string Key { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, NestGeometry> Nests { get; set; } = new();
    }

    // Géométrie d'une zone dans un profil, en DIPs (même espace que NestData).
    public class NestGeometry
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    // Modèle de zone : tout ce qui définit son look, réutilisable pour créer une nouvelle zone.
    public class NestTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#1E1E2E";
        public double Opacity { get; set; } = 0.75;
        public int BackgroundMode { get; set; }
        public string GradientStart { get; set; } = "#1E1E2E";
        public string GradientEnd { get; set; } = "#4A3426";
        public double GradientAngle { get; set; } = 45;
        public string BackgroundImage { get; set; } = "";
        public int IconSize { get; set; } = 40;
        public bool ListView { get; set; }
        public double Width { get; set; } = 320;
        public double Height { get; set; } = 240;
        public bool TitleVisible { get; set; } = true;
        public string TitleFont { get; set; } = "";
        public int TitleFontSize { get; set; } = 13;
        public string ItemFont { get; set; } = "";
        public int ItemFontSize { get; set; } = 11;
    }

    public class MovedItem
    {
        public string StorePath { get; set; } = "";
        public string OriginalPath { get; set; } = "";
    }

    public class AppSettings
    {
        public bool Enabled { get; set; } = true;                 // DeskNest actif (zones affichées)
        public bool HotkeyEnabled { get; set; } = true;           // raccourci de masquage actif
        public string ToggleHotkey { get; set; } = "Ctrl+Alt+F";  // masquer / afficher les zones
        public string SearchHotkey { get; set; } = "Ctrl+Shift+Space"; // palette de recherche globale
        public bool DesktopDoubleClickEnabled { get; set; } = true;
        public bool HideDesktopIcons { get; set; } = true;
        public bool ShowSearchBar { get; set; } = true;           // barre de recherche sous la barre de titre des zones
        public bool SnapEnabled { get; set; } = true;             // aimantation et guides d'alignement pendant le déplacement
        public bool AutoUpdate { get; set; } = true;              // vérifier les mises à jour au démarrage
        public int DesktopMode { get; set; } = 0;                 // 0 = déplacer dans le dossier géré, 1 = seulement cacher
        public bool AnimateRoll { get; set; } = true;             // animation du Roll
        public int RollAnimationMs { get; set; } = 180;
        public bool HoverExpand { get; set; } = true;            // développer au survol (zones réduites)
        public int HoverExpandDelayMs { get; set; } = 250;
        public int HoverCollapseDelayMs { get; set; } = 500;
        public bool ShowThumbnails { get; set; } = true;         // miniatures des images, vidéos et PDF
        public bool OpenItemsWithSingleClick { get; set; }       // ouvrir les éléments d'un simple clic (décoché par défaut)
        public string DefaultColor { get; set; } = "#1E1E2E";
        public double DefaultOpacity { get; set; } = 0.75;
        public int DefaultIconSize { get; set; } = 40;
        public int CornerRadius { get; set; } = 10;
        public bool ShowBorder { get; set; } = true;
        public string Language { get; set; } = "";               // langue de l'interface (vide = langue de Windows si prise en charge, sinon anglais)

        // Notes (valeurs globales ; le texte lui-même est stocké hors de layout.json).
        // double : FontSize est un DependencyProperty double (voir FontSizeHelper).
        [JsonConverter(typeof(FontSizeJsonConverter))]
        public double NotesFontSize { get; set; } = 13;
        public bool NotesWrap { get; set; } = true;
        public int NotesSaveDelayMs { get; set; } = 800;

        // To-do (valeurs globales, sans surcharge par zone).
        [JsonConverter(typeof(FontSizeJsonConverter))]
        public double TodoFontSize { get; set; } = 12;
        public bool TodoStrikeThrough { get; set; } = true;
        public bool TodoCheckedToBottom { get; set; }
        public bool TodoAddAtTop { get; set; }

        // Affiche la rangée d'actions rapides dans la palette de recherche.
        public bool ShowQuickActionsInSearch { get; set; } = true;
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
        public int IconPixels { get; }

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

        // Un tri actif remplace l'ordre manuel : le glisser-déposer de
        // réordonnancement est désactivé et l'infobulle l'explique.
        public bool ReorderEnabled { get; set; } = true;
        public string ItemToolTip => ReorderEnabled ? FullPath : Loc.T("sort.reorderHint");

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

        public ItemVM(string path, int iconSize, int iconPixels = 0, bool isPortal = false, bool isUp = false, bool showThumbnails = true)
        {
            FullPath = path;
            IconSize = iconSize;
            IconPixels = iconPixels > 0 ? iconPixels : iconSize;
            IsUp = isUp;
            IsFolder = isUp || Directory.Exists(path);
            CanRemove = !isPortal && !isUp;
            if (isUp) Name = "..";
            else Name = GetName(path);

            // Les images, vidéos et PDF affichent une miniature ; tout le reste garde l'icône.
            if (showThumbnails && IconHelper.CanThumbnail(FullPath))
            {
                Icon = IconHelper.GetCachedThumbnail(FullPath, IconPixels);
                if (Icon == null) LoadThumbnailAsync();
            }
            else
            {
                Icon = IconHelper.GetCached(FullPath, IconPixels);
                if (Icon == null) LoadIconAsync();
            }
        }

        // Nom affiché dans une zone (sans l'extension pour les fichiers, comme l'Explorateur).
        public static string GetName(string path)
        {
            var n = Directory.Exists(path)
                ? System.IO.Path.GetFileName(path.TrimEnd('\\'))
                : System.IO.Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(n) ? path : n;
        }

        private async void LoadIconAsync()
        {
            try
            {
                var icon = await IconHelper.GetIconAsync(FullPath, IconPixels);
                if (icon != null && _icon == null) Icon = icon;
            }
            catch { }
        }

        private async void LoadThumbnailAsync()
        {
            try
            {
                var icon = await IconHelper.GetThumbnailAsync(FullPath, IconPixels);
                // Pas de fournisseur de miniature (PDF sans extension, codec absent…) : icône du shell.
                if (icon == null) icon = await IconHelper.GetIconAsync(FullPath, IconPixels);
                if (icon != null && _icon == null) Icon = icon;
            }
            catch { }
        }
    }
}
