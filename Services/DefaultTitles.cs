using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskNest
{
    // Titres par défaut des zones : « Zone N », « Todo N », « Note N ».
    // Le préfixe est localisé comme le reste de l'interface ; le numéro est le
    // plus petit entier encore libre parmi les zones du même type.
    public static class DefaultTitles
    {
        public static string KeyFor(string? nestType) => nestType switch
        {
            NestTypes.Todo => "todo.default",
            NestTypes.Notes => "note.default",
            _ => "zone.default"
        };

        public static int NextNumber(string key, IEnumerable<string?> existingTitles)
        {
            var taken = new HashSet<string>(
                existingTitles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!),
                StringComparer.Ordinal);

            for (int n = 1; n < int.MaxValue; n++)
                if (!taken.Contains(Loc.F(key, n))) return n;
            return 1;
        }
    }
}
