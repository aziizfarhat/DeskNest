using System;
using System.Linq;
using Microsoft.Win32;

namespace DeskNest
{
    public sealed partial class NestManager
    {
        private void SubscribeDisplayChanges()
        {
            try
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            catch (Exception ex) { Diag.Log("suivi écrans : " + ex.Message); }
        }

        private void UnsubscribeDisplayChanges()
        {
            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; }
            catch (Exception ex) { Diag.Log("suivi écrans : " + ex.Message); }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e) => QueueDisplayProfileRefresh();

        private void QueueDisplayProfileRefresh()
        {
            // Un branchement d'écran émet plusieurs événements : on ne garde que le dernier.
            _displayTimer.Stop();
            _displayTimer.Start();
        }

        private void RefreshDisplayProfile()
        {
            _displayTimer.Stop();
            // Changer la géométrie pendant un glisser ou un redimensionnement casserait
            // l'interaction en cours : on attend la fin avec un nouveau délai.
            if (_windows.Any(w => w.IsBusy))
            {
                Diag.Log("profils : changement d'écran reporté (interaction en cours)");
                _displayTimer.Start();
                return;
            }
            ApplyDisplayProfileFromCurrentMonitors();
        }

        private void ApplyDisplayProfileFromCurrentMonitors()
        {
            var result = DisplayProfileSwitcher.Switch(_data, DisplayProfiles.CurrentMonitors());
            if (!result.Changed) return;

            Diag.Log($"profils : bascule vers {_data.ActiveProfileKey} " +
                     $"(profil exact={result.ProfileFound}, adaptation={result.Adapted})");
            foreach (var w in _windows.ToList())
            {
                w.ApplyExternalChange();
                // Reprend les valeurs vivantes déjà calculées par le switcher, y compris
                // pour un profil exact : le recadrage du constructeur doit être ignoré.
                w.SetBounds(w.Data.Left, w.Data.Top, w.Data.Width, w.Data.Height);
            }
            Storage.SaveNow(_data);
        }
    }
}
