using System;
using System.Windows.Media;

namespace DeskNest
{
    // Géométries d'icônes de zone, définies une seule fois et partagées entre la
    // barre de titre et les actions rapides. Elles sont figées : aucun rendu ne
    // peut les modifier et elles ne coûtent qu'une allocation par type.
    public static class NestIcons
    {
        private static Geometry Make(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }

        // Zone Fichiers (grille) et Folder Portal (dossier) : dessins historiques, inchangés.
        public static readonly Geometry Files = Make("M1,1 H7 V7 H1 Z M9,1 H15 V7 H9 Z M1,9 H7 V15 H1 Z M9,9 H15 V15 H9 Z");
        public static readonly Geometry Portal = Make("M1,3 H6 L8,5 H15 V13 H1 Z");

        // To-do : checklist (trois cases et trois lignes).
        public static readonly Geometry Todo = Make(
            "M2,2 H5 V5 H2 Z M7,2.5 H14 V4 H7 Z " +
            "M2,7 H5 V10 H2 Z M7,7.5 H14 V9 H7 Z " +
            "M2,12 H5 V15 H2 Z M7,12.5 H12 V14 H7 Z");

        // Notes : feuille au coin replié (F0 = remplissage pair-impair pour le rabat).
        public static readonly Geometry Notes = Make("F0 M2,1 H11 L15,5 V15 H2 Z M11,1 V5 H15 Z");

        // Le portail garde son icône dossier, quel que soit le type de la zone.
        public static Geometry For(string? nestType, bool portal) => portal
            ? Portal
            : nestType switch
            {
                NestTypes.Todo => Todo,
                NestTypes.Notes => Notes,
                _ => Files
            };

        // Icônes des actions rapides : seules les deux nouvelles zones ont une géométrie
        // partagée ; les autres actions gardent leur glyphe Segoe.
        public static Geometry? ForAction(QuickActionKind kind) => kind switch
        {
            QuickActionKind.NewTodo => Todo,
            QuickActionKind.NewNote => Notes,
            _ => null
        };
    }
}
