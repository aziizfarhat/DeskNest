using System;

namespace DeskNest
{
    // Modes de tri d'une zone Fichiers ou d'un portail. « Manual » conserve
    // l'ordre existant (NestData.Items / PortalOrder) et n'écrit jamais dessus.
    public static class NestSort
    {
        public const string Manual = "Manual";
        public const string Name = "Name";
        public const string Type = "Type";
        public const string Size = "Size";
        public const string DateCreated = "DateCreated";
        public const string DateModified = "DateModified";

        public static readonly string[] Modes = { Manual, Name, Type, Size, DateCreated, DateModified };

        public static bool IsKnown(string? mode) => mode is Manual or Name or Type or Size or DateCreated or DateModified;

        public static bool IsActive(string? mode) => IsKnown(mode) && mode != Manual;

        public static string Normalize(string? mode) => IsKnown(mode) ? mode! : Manual;

        public static string LabelKey(string? mode) => mode switch
        {
            Name => "sort.name",
            Type => "sort.type",
            Size => "sort.size",
            DateCreated => "sort.dateCreated",
            DateModified => "sort.dateModified",
            _ => "sort.manual"
        };
    }
}
