using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Windows;

namespace DeskNest
{
    public sealed class ClipData
    {
        public List<string> Paths { get; } = new();
        public bool Cut { get; set; }
        public Guid? SourceNest { get; set; }   // zone d'où vient un « Couper » de DeskNest
    }

    // Presse-papiers de fichiers compatible avec l'Explorateur (copier / couper / coller).
    public static class FileClipboard
    {
        public static bool HasFiles()
        {
            try { return Clipboard.ContainsFileDropList(); } catch { return false; }
        }

        public static void Write(IEnumerable<string> paths, bool cut, Guid? nestId)
        {
            var sc = new StringCollection();
            foreach (var p in paths) sc.Add(p);
            var data = new DataObject();
            data.SetFileDropList(sc);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            if (cut && nestId.HasValue) data.SetData("DeskNest.Cut", nestId.Value.ToString());
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(60); } // presse-papiers occupé
            }
        }

        public static ClipData? Read()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return null;
                var clip = new ClipData();
                foreach (var p in Clipboard.GetFileDropList()) if (p != null) clip.Paths.Add(p);
                if (clip.Paths.Count == 0) return null;

                if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4)
                {
                    var b = new byte[4];
                    ms.Position = 0;
                    ms.Read(b, 0, 4);
                    clip.Cut = BitConverter.ToInt32(b, 0) == 2;
                }
                if (clip.Cut)
                {
                    var cutData = Clipboard.GetData("DeskNest.Cut") as string
                        ?? Clipboard.GetData("NestsLite.Cut") as string;
                    if (cutData != null && Guid.TryParse(cutData, out var g))
                        clip.SourceNest = g;
                }
                return clip;
            }
            catch { return null; }
        }

        public static void Clear()
        {
            try { Clipboard.Clear(); } catch { }
        }

        public static void WriteText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); return; }
                catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(60); }
            }
        }
    }
}
