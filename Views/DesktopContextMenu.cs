using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeskNest
{
    public sealed class DesktopContextMenu : IDisposable
    {
        private const string MenuKey =
            @"Software\Classes\DesktopBackground\Shell\DeskNest";

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        private bool _registered;
        private bool _disposed;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(
            int eventId,
            uint flags,
            IntPtr item1,
            IntPtr item2);

        public DesktopContextMenu()
        {
            // Les entrées sont persistantes : écrites par l'installateur, puis
            // actualisées ici (libellés et chemin) au démarrage. On ne supprime
            // plus l'arborescence, sinon on effacerait les entrées de
            // l'installateur à chaque mise à jour.
            Loc.Changed += OnLanguageChanged;

            Register();
        }

        private void Register()
        {
            try
            {
                WriteMenu();
                _registered = true;
                NotifyExplorer();

                Diag.Log("Menu contextuel DeskNest enregistré.");
            }
            catch (Exception ex)
            {
                // Ne pas détruire les entrées installer si l'actualisation échoue.
                Diag.Log("Menu contextuel (enregistrement) : " + ex);
            }
        }

        private void OnLanguageChanged()
        {
            if (!_registered || _disposed)
                return;

            try
            {
                WriteMenu();
                NotifyExplorer();

                Diag.Log("Menu contextuel DeskNest actualisé.");
            }
            catch (Exception ex)
            {
                Diag.Log("Menu contextuel (traduction) : " + ex);
            }
        }

        private static void WriteMenu()
        {
            var exe = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException(
                    "Impossible de déterminer le chemin de DeskNest.exe.");

            using var root = Registry.CurrentUser.CreateSubKey(MenuKey, true);

            if (root == null)
                throw new InvalidOperationException(
                    "Impossible de créer la clé du menu DeskNest.");

            root.SetValue("MUIVerb", "DeskNest", RegistryValueKind.String);
            root.SetValue("SubCommands", "", RegistryValueKind.String);
            root.SetValue("Icon", $"\"{exe}\",0", RegistryValueKind.String);

            using var shell = root.CreateSubKey("Shell", true);

            if (shell == null)
                throw new InvalidOperationException(
                    "Impossible de créer les commandes du menu.");

            WriteCommand(
                shell,
                "NewZone",
                Loc.T("tray.newZone"),
                exe,
                DeskNestCommands.NewZone);

            WriteCommand(
                shell,
                "NewPortal",
                Loc.T("tray.newPortal"),
                exe,
                DeskNestCommands.NewPortal);

            WriteCommand(
                shell,
                "NewTodo",
                Loc.T("tray.newTodo"),
                exe,
                DeskNestCommands.NewTodo);

            WriteCommand(
                shell,
                "NewNote",
                Loc.T("tray.newNote"),
                exe,
                DeskNestCommands.NewNote);
        }

        private static void WriteCommand(
            RegistryKey shell,
            string keyName,
            string label,
            string exe,
            string argument)
        {
            using var verb = shell.CreateSubKey(keyName, true);

            if (verb == null)
                throw new InvalidOperationException(
                    "Impossible de créer la commande : " + keyName);

            verb.SetValue("", label, RegistryValueKind.String);

            using var command = verb.CreateSubKey("command", true);

            if (command == null)
                throw new InvalidOperationException(
                    "Impossible de créer la commande Windows : " + keyName);

            command.SetValue(
                "",
                $"\"{exe}\" {argument}",
                RegistryValueKind.String);
        }

        private static void NotifyExplorer()
        {
            try
            {
                SHChangeNotify(
                    SHCNE_ASSOCCHANGED,
                    SHCNF_IDLIST,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Diag.Log("Actualisation du menu Windows : " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Loc.Changed -= OnLanguageChanged;
            // Les entrées restent en place : seul le désinstallateur les supprime
            // (Flags: uninsdeletekey dans installer.iss).
        }
    }
}