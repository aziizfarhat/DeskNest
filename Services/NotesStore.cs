using System;
using System.Collections.Generic;
using System.IO;

namespace DeskNest
{
    // Persistance des notes : un fichier UTF-8 par zone, stocké hors de layout.json.
    // Les écritures sont atomiques (fichier temporaire + Replace). Les brouillons
    // d'édition utilisent le même mécanisme dans un fichier « .draft.txt » séparé :
    // la note réelle n'est jamais modifiée pendant la saisie.
    public static class NotesStore
    {
        public const int MaxChars = 100000;

        private static readonly object Gate = new();
        private static readonly Dictionary<Guid, string> Pending = new();

        public static string NotesDirectory => Path.Combine(Storage.Dir, "Notes");

        public static string PathFor(Guid nestId) => Path.Combine(NotesDirectory, nestId.ToString("N") + ".txt");

        public static string DraftPathFor(Guid nestId) =>
            Path.Combine(NotesDirectory, nestId.ToString("N") + ".draft.txt");

        // Un fichier absent est une note vide. Un fichier trop long est tronqué à la limite.
        public static string Load(Guid nestId) => Read(PathFor(nestId));

        public static string LoadDraft(Guid nestId) => Read(DraftPathFor(nestId));

        private static string Read(string path)
        {
            try
            {
                var fs = Storage.TextFileSystem;
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

        public static bool HasDraft(Guid nestId)
        {
            try { return Storage.TextFileSystem.FileExists(DraftPathFor(nestId)); }
            catch { return false; }
        }

        public static bool Save(Guid nestId, string text)
        {
            text ??= "";
            try
            {
                WriteAtomic(PathFor(nestId), text);
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

        // Brouillon d'une note en cours d'édition. Aucun suivi « Pending » ici :
        // le texte reste de toute façon dans le champ et sera réécrit plus tard.
        public static bool SaveDraft(Guid nestId, string text)
        {
            try
            {
                WriteAtomic(DraftPathFor(nestId), text ?? "");
                return true;
            }
            catch (Exception ex)
            {
                Diag.Log("notes : brouillon impossible pour " + nestId + " : " + ex.Message);
                return false;
            }
        }

        // Fait du brouillon la note officielle, puis le supprime.
        public static bool PromoteDraft(Guid nestId, string text)
        {
            bool ok = Save(nestId, text);
            if (ok) DeleteDraft(nestId);
            return ok;
        }

        public static void DeleteDraft(Guid nestId) =>
            DeleteFiles(DraftPathFor(nestId), DraftPathFor(nestId) + ".tmp", DraftPathFor(nestId) + ".bak");

        public static void Delete(Guid nestId)
        {
            var path = PathFor(nestId);
            DeleteFiles(path, path + ".bak", path + ".tmp",
                DraftPathFor(nestId), DraftPathFor(nestId) + ".tmp", DraftPathFor(nestId) + ".bak");
            lock (Gate) Pending.Remove(nestId);
        }

        private static void DeleteFiles(params string[] paths)
        {
            var fs = Storage.TextFileSystem;
            foreach (var p in paths)
            {
                try { if (fs.FileExists(p)) fs.Delete(p); }
                catch (Exception ex) { Diag.Log("notes : suppression impossible : " + ex.Message); }
            }
        }

        private static void WriteAtomic(string path, string text)
        {
            if (text.Length > MaxChars) text = text.Substring(0, MaxChars);
            var fs = Storage.TextFileSystem;
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
