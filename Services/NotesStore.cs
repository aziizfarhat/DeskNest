using System;
using System.Collections.Generic;
using System.IO;

namespace DeskNest
{
    // Persistance des notes : un fichier UTF-8 par zone, stocké hors de layout.json.
    // Les écritures sont atomiques (fichier temporaire + Replace). Si l'écriture
    // échoue, le texte est gardé en mémoire et sera réessayé plus tard.
    public static class NotesStore
    {
        public const int MaxChars = 100000;

        private static readonly object Gate = new();
        private static readonly Dictionary<Guid, string> Pending = new();

        public static string NotesDirectory => Path.Combine(Storage.Dir, "Notes");

        public static string PathFor(Guid nestId) => Path.Combine(NotesDirectory, nestId.ToString("N") + ".txt");

        // Un fichier absent est une note vide. Un fichier trop long est tronqué à la limite.
        public static string Load(Guid nestId)
        {
            try
            {
                var fs = Storage.TextFileSystem;
                var path = PathFor(nestId);
                if (!fs.FileExists(path)) return "";
                var text = fs.ReadAllText(path) ?? "";
                return text.Length > MaxChars ? text.Substring(0, MaxChars) : text;
            }
            catch (Exception ex)
            {
                Diag.Log("notes : lecture impossible : " + ex.Message);
                return "";
            }
        }

        public static bool Save(Guid nestId, string text)
        {
            text ??= "";
            if (text.Length > MaxChars) text = text.Substring(0, MaxChars);
            try
            {
                var fs = Storage.TextFileSystem;
                var path = PathFor(nestId);
                fs.CreateDirectory(NotesDirectory);
                var tmp = path + ".tmp";
                fs.WriteAllText(tmp, text);

                // Remplacement atomique : le fichier lisible n'est jamais partiel.
                if (fs.FileExists(path))
                {
                    try { fs.Replace(tmp, path, path + ".bak", true); }
                    catch
                    {
                        // Système de fichiers ne gérant pas Replace : on garde le nouveau texte.
                        fs.Copy(tmp, path, true);
                        try { fs.Delete(tmp); } catch { }
                    }
                }
                else fs.Move(tmp, path);

                lock (Gate) Pending.Remove(nestId);
                return true;
            }
            catch (Exception ex)
            {
                Diag.Log("notes : sauvegarde impossible pour " + nestId + " : " + ex.Message);
                lock (Gate) Pending[nestId] = text;
                return false;
            }
        }

        public static void Delete(Guid nestId)
        {
            var fs = Storage.TextFileSystem;
            var path = PathFor(nestId);
            foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            {
                try { if (fs.FileExists(p)) fs.Delete(p); }
                catch (Exception ex) { Diag.Log("notes : suppression impossible : " + ex.Message); }
            }
            lock (Gate) Pending.Remove(nestId);
        }

        // Le texte d'une écriture échouée reste en mémoire jusqu'à ce qu'un
        // nouvel essai réussisse (fermeture, sortie ou prochaine saisie).
        public static void RetryPending()
        {
            List<KeyValuePair<Guid, string>> waiting;
            lock (Gate)
            {
                if (Pending.Count == 0) return;
                waiting = new List<KeyValuePair<Guid, string>>(Pending);
            }
            foreach (var item in waiting) Save(item.Key, item.Value);
        }
    }
}
