namespace DeskNest
{
    // Verbes de ligne de commande partagés par le menu du bureau, le serveur
    // de commandes et les tests. Une seule liste pour éviter qu'un verbe soit
    // accepté à un endroit et refusé à un autre.
    public static class DeskNestCommands
    {
        public const string NewZone = "--new-zone";
        public const string NewPortal = "--new-portal";
        public const string NewTodo = "--new-todo";
        public const string NewNote = "--new-note";

        public static bool IsKnown(string? command) =>
            command is NewZone or NewPortal or NewTodo or NewNote;

        public static QuickActionKind? QuickAction(string? command) => command switch
        {
            NewZone => QuickActionKind.NewZone,
            NewPortal => QuickActionKind.NewPortal,
            NewTodo => QuickActionKind.NewTodo,
            NewNote => QuickActionKind.NewNote,
            _ => null
        };
    }
}
