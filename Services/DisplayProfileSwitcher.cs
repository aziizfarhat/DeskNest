using System;
using System.Collections.Generic;

namespace DeskNest
{
    public sealed class DisplaySwitchResult
    {
        public bool Changed { get; }
        public bool ProfileFound { get; }
        public bool Adapted { get; }

        public DisplaySwitchResult(bool changed, bool profileFound, bool adapted)
        {
            Changed = changed;
            ProfileFound = profileFound;
            Adapted = adapted;
        }
    }

    // Bascule d'une configuration d'écrans à une autre. Les valeurs vivantes sont
    // enregistrées dans l'ANCIEN profil avant toute modification, puis le profil
    // cible est appliqué tel quel (ou complété/adapté par D4).
    public static class DisplayProfileSwitcher
    {
        public static DisplaySwitchResult Switch(AppData data, IReadOnlyList<MonitorDescriptor> monitors)
        {
            DisplayProfiles.NormalizeProfiles(data);
            var newKey = DisplayProfiles.ComputeKey(monitors);
            var oldKey = data.ActiveProfileKey;
            var changed = !string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase);
            DisplayProfiles.RememberMonitors(data, newKey, monitors);
            if (!changed) return new DisplaySwitchResult(false, DisplayProfiles.FindProfile(data, newKey) != null, false);

            // Étape critique : ces positions décrivent encore l'ancien écran.
            var oldProfile = DisplayProfiles.FindProfile(data, oldKey);
            if (oldProfile != null)
            {
                oldProfile.Nests = DisplayProfiles.CaptureGeometry(data.Nests);
                Diag.Log($"profils : positions enregistrées dans l'ancien profil {oldKey}");
            }

            var sourceProfile = DisplayAdaptation.SelectSourceProfile(data, monitors, oldProfile);
            var sourceMonitors = sourceProfile == null
                ? Array.Empty<MonitorDescriptor>()
                : DisplayProfiles.GetMonitorsFor(data, sourceProfile.Key);
            if (sourceMonitors.Count == 0) sourceMonitors = monitors;

            var target = DisplayProfiles.FindProfile(data, newKey);
            var adapted = false;
            if (target != null)
            {
                var missing = 0;
                foreach (var nest in data.Nests)
                {
                    if (target.Nests.TryGetValue(nest.Id.ToString(), out var geometry))
                    {
                        DisplayProfiles.ApplyGeometry(nest, geometry);
                    }
                    else
                    {
                        missing++;
                        DisplayAdaptation.AdaptMissing(nest, monitors, sourceProfile, sourceMonitors);
                    }
                }

                if (missing > 0)
                {
                    adapted = true;
                    target.Nests = DisplayProfiles.CaptureGeometry(data.Nests);
                    Diag.Log($"profils : {missing} zone(s) adaptée(s) dans le nouveau profil {newKey}");
                }
            }
            else
            {
                adapted = true;
                DisplayAdaptation.Adapt(data, monitors, sourceProfile, sourceMonitors);
                target = new DisplayProfile
                {
                    Key = newKey,
                    Description = DisplayProfiles.BuildDescription(monitors),
                    Nests = DisplayProfiles.CaptureGeometry(data.Nests)
                };
                data.Profiles.Add(target);
                Diag.Log($"profils : nouveau profil créé pour {newKey}");
            }

            data.ActiveProfileKey = newKey;
            data.Version = 3;
            return new DisplaySwitchResult(true, true, adapted);
        }
    }
}
