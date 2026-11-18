using System;
using System.Collections.Generic;

namespace DeskNest
{
    // Point d'entrée de la règle D4. D3 appelle ce service quand aucune disposition
    // exacte n'existe ; l'algorithme d'ancrage/cadrage est fourni à l'étape D4.
    public static class DisplayAdaptation
    {
        public static void Adapt(AppData data, IReadOnlyList<MonitorDescriptor> monitors, DisplayProfile? source)
        {
            Diag.Log("profils : configuration inconnue, adaptation différée");
        }

        public static void AdaptMissing(NestData nest, IReadOnlyList<MonitorDescriptor> monitors, DisplayProfile? source)
        {
            Diag.Log($"profils : zone '{nest.Title}' absente du profil, adaptation différée");
        }
    }
}
