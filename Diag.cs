using System;
using System.IO;

namespace DeskNest
{
    // Petit journal de diagnostic (%APPDATA%\DeskNest\diagnostic.log), limité en taille.
    public static class Diag
    {
        private static readonly object Gate = new();

        public static string LogPath => System.IO.Path.Combine(Storage.Dir, "diagnostic.log");

        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Storage.Dir);
                    var p = LogPath;
                    if (File.Exists(p) && new FileInfo(p).Length > 200_000) File.Delete(p);
                    File.AppendAllText(p, $"{DateTime.Now:HH:mm:ss.fff}  {message}\n");
                }
            }
            catch { }
        }
    }
}
