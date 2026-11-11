using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DeskNest
{
    public sealed class TrayIcon : IDisposable
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "DeskNest";
        private const string LegacyRunName = "NestsLite";
        private readonly NestManager _manager;
        private readonly System.Windows.Forms.NotifyIcon _ni;
        private readonly System.Drawing.Icon? _icon;

        public TrayIcon(NestManager manager)
        {
            _manager = manager;
            _icon = LoadAppIcon();
            _ni = new System.Windows.Forms.NotifyIcon
            {
                Icon = _icon ?? System.Drawing.SystemIcons.Application,
                Text = "DeskNest",
                Visible = true
            };
            _ni.DoubleClick += (_, _) => manager.ToggleHidden();
            IsAutostart(); // migration éventuelle de l'ancien nom dans la clé Run
            BuildMenu();
            Loc.Changed += BuildMenu; // le menu suit la langue choisie
        }

        // Icône du programme (celle de l'exécutable), avec repli sur l'icône générique de Windows.
        private static System.Drawing.Icon? LoadAppIcon()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) return System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
            catch (Exception ex) { Diag.Log("icône de la zone de notification : " + ex.Message); }
            return null;
        }

        private void BuildMenu()
        {
            var old = _ni.ContextMenuStrip;
            var menu = new System.Windows.Forms.ContextMenuStrip
            {
                RightToLeft = Loc.IsRtl ? System.Windows.Forms.RightToLeft.Yes : System.Windows.Forms.RightToLeft.No
            };
            menu.Items.Add(Loc.T("tray.settings"), null, (_, _) => _manager.ShowSettings());
            menu.Items.Add(Loc.T("tray.templates"), null, (_, _) => _manager.ShowTemplates());
            menu.Items.Add(Loc.T("tray.search"), null, (_, _) => _manager.ShowSearchPalette());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var undo = new System.Windows.Forms.ToolStripMenuItem(Loc.T("menu.undo"), null, (_, _) => _manager.Undo())
            { ShortcutKeyDisplayString = "Ctrl+Z" };
            var redo = new System.Windows.Forms.ToolStripMenuItem(Loc.T("menu.redo"), null, (_, _) => _manager.Redo())
            { ShortcutKeyDisplayString = "Ctrl+Y" };
            menu.Items.Add(undo);
            menu.Items.Add(redo);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            menu.Items.Add(Loc.T("tray.newZone"), null, (_, _) => _manager.CreateNest());
            menu.Items.Add(Loc.T("tray.newPortal"), null, (_, _) => _manager.CreatePortal());

            var fromTemplate = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.newFromTemplate"));
            if (_manager.Templates.Count == 0)
            {
                fromTemplate.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(Loc.T("tpl.none")) { Enabled = false });
            }
            else
            {
                foreach (var template in _manager.Templates)
                {
                    var tpl = template;
                    fromTemplate.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(tpl.Name, null,
                        (_, _) => _manager.CreateNestFromTemplate(tpl)));
                }
                fromTemplate.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
                var delete = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tpl.delete"));
                foreach (var template in _manager.Templates)
                {
                    var tpl = template;
                    delete.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem(tpl.Name, null, (_, _) =>
                    {
                        var r = System.Windows.MessageBox.Show(Loc.F("tpl.deleteConfirm", tpl.Name), "DeskNest",
                            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question,
                            System.Windows.MessageBoxResult.No, Loc.MsgOptions);
                        if (r == System.Windows.MessageBoxResult.Yes)
                        {
                            _manager.DeleteTemplate(tpl);
                            BuildMenu();
                        }
                    }));
                }
                fromTemplate.DropDownItems.Add(delete);
            }
            menu.Items.Add(fromTemplate);

            menu.Items.Add(Loc.T("tray.toggle"), null, (_, _) => _manager.ToggleHidden());

            var snap = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.snap"));
            snap.Click += (_, _) =>
            {
                _manager.Settings.SnapEnabled = !_manager.Settings.SnapEnabled;
                _manager.SettingsChanged();
            };
            menu.Items.Add(snap);
            menu.Items.Add(Loc.T("tray.distribute"), null, (_, _) => _manager.DistributeNests());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var autostart = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.autostart"));
            autostart.Click += (_, _) => SetAutostart(!IsAutostart());
            menu.Items.Add(autostart);
            menu.Items.Add(Loc.T("tray.updates"), null, async (_, _) => await _manager.CheckForUpdatesAsync(false));
            menu.Items.Add(Loc.T("tray.restoreIcons"), null, (_, _) =>
            {
                var r = System.Windows.MessageBox.Show(Loc.T("tray.restoreConfirm"), "DeskNest",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question,
                    System.Windows.MessageBoxResult.No, Loc.MsgOptions);
                if (r == System.Windows.MessageBoxResult.Yes) _manager.RestoreAllHidden();
            });
            menu.Items.Add(Loc.T("tray.openConfig"), null, (_, _) =>
                Process.Start(new ProcessStartInfo(Storage.Dir) { UseShellExecute = true }));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(Loc.T("tray.quit"), null, (_, _) => System.Windows.Application.Current.Shutdown());
            menu.Opening += (_, _) =>
            {
                autostart.Checked = IsAutostart();
                snap.Checked = _manager.Settings.SnapEnabled;
                undo.Enabled = _manager.History.CanUndo;
                redo.Enabled = _manager.History.CanRedo;
            };

            _ni.ContextMenuStrip = menu;
            old?.Dispose();
        }

        public static bool IsAutostart()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (k == null) return false;
                if (k.GetValue(RunName) != null)
                {
                    k.DeleteValue(LegacyRunName, false);
                    return true;
                }

                // Migre l'ancien démarrage automatique vers le nouvel identifiant.
                if (k.GetValue(LegacyRunName) != null)
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe)) k.SetValue(RunName, $"\"{exe}\"");
                    k.DeleteValue(LegacyRunName, false);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Diag.Log("démarrage auto (lecture) : " + ex.Message);
                return false;
            }
        }

        public static void SetAutostart(bool enable)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (k == null) return;
                k.DeleteValue(LegacyRunName, false);
                if (enable) k.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
                else k.DeleteValue(RunName, false);
            }
            catch (Exception ex)
            {
                Diag.Log("démarrage auto (écriture) : " + ex.Message);
            }
        }

        public void Dispose()
        {
            Loc.Changed -= BuildMenu;
            _ni.Visible = false;
            _ni.Dispose();
            _icon?.Dispose();
        }
    }
}
