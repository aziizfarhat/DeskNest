using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeskNest
{
    // Textes de l'application : une seule langue est matérialisée à la fois.
    // Les tables sont intégrées à l'assembly (Localization\*.json) pour éviter de
    // charger les douze langues et leurs 255 clés dans chaque processus.
    public static class Loc
    {
        // Ordre des traductions : en, fr, it, de, es, pt, ar, zh, ja, ko, ru, uk
        public static readonly (string Code, string Name)[] Languages =
        {
            ("en", "English"), ("fr", "Français"), ("it", "Italiano"), ("de", "Deutsch"), ("es", "Español"),
            ("pt", "Português"), ("ar", "العربية"), ("zh", "中文 (简体)"), ("ja", "日本語"), ("ko", "한국어"),
            ("ru", "Русский"), ("uk", "Українська")
        };

        private static int _idx;
        private static Dictionary<string, string>? _strings;
        private static Dictionary<string, string>? _english;
        private static Dictionary<string, string>? _defaultTitles;

        public static string Current => Languages[_idx].Code;
        public static bool IsRtl => Current == "ar";
        public static event Action? Changed;

        public static System.Windows.MessageBoxOptions MsgOptions => IsRtl
            ? System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign
            : System.Windows.MessageBoxOptions.None;
        public static System.Windows.FlowDirection Flow => IsRtl
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;

        public static void SetLanguage(string? code)
        {
            var resolved = ResolveLanguage(code);
            int i = Array.FindIndex(Languages, l => string.Equals(l.Code, resolved, StringComparison.OrdinalIgnoreCase));
            if (i < 0) i = 0;
            if (i == _idx)
            {
                _strings ??= LoadTable(Current);
                return;
            }
            _idx = i;
            _strings = LoadTable(Current);
            Changed?.Invoke();
        }

        // Langue à utiliser : code explicite s'il est pris en charge, sinon langue
        // de l'interface Windows, sinon anglais. Un code vide signifie « automatique ».
        public static string ResolveLanguage(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                int i = IndexOf(code);
                return i >= 0 ? Languages[i].Code : "en";
            }

            foreach (var culture in new[] { CultureInfo.CurrentUICulture, CultureInfo.InstalledUICulture, CultureInfo.CurrentCulture })
            {
                if (culture == null) continue;
                int i = IndexOf(culture.TwoLetterISOLanguageName);
                if (i >= 0) return Languages[i].Code;
            }
            return "en";
        }

        private static int IndexOf(string? code) =>
            string.IsNullOrWhiteSpace(code)
                ? -1
                : Array.FindIndex(Languages, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

        private static Dictionary<string, string> LoadTable(string name)
        {
            try
            {
                using var stream = typeof(Loc).Assembly.GetManifestResourceStream("DeskNest.Localization." + name + ".json");
                if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                       ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                Diag.Log("localisation " + name + " : " + ex.Message);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static Dictionary<string, string> Strings => _strings ??= LoadTable(Current);

        private static Dictionary<string, string> English => _english ??= LoadTable("en");

        public static string T(string key)
        {
            if (Strings.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s)) return s;
            if (English.TryGetValue(key, out var e) && !string.IsNullOrEmpty(e)) return e;
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(T(key), args);

        // Vrai si le titre est un nom de zone par défaut (« Zone 3 », « Bereich 3 »...), dans n'importe quelle langue.
        public static bool IsDefaultZoneTitle(string title)
        {
            if (title == "Nouvelle zone") return true;
            _defaultTitles ??= LoadTable("defaultTitles");
            foreach (var fmt in _defaultTitles.Values.Distinct())
            {
                if (string.IsNullOrEmpty(fmt)) continue;
                var pattern = "^" + Regex.Escape(fmt).Replace("\\{0}", "\\d+") + "$";
                if (Regex.IsMatch(title, pattern)) return true;
            }
            return false;
        }
    }
}
