namespace DeskNest
{
    public sealed partial class NestManager
    {
        // Exécution commune aux deux fenêtres qui affichent QuickActionsBar.
        public void ExecuteQuickAction(QuickActionKind kind)
        {
            switch (kind)
            {
                case QuickActionKind.NewZone:
                    CreateNestAtCursor(NestTypes.Files);
                    break;
                case QuickActionKind.NewPortal:
                    CreatePortalAtCursor();
                    break;
                case QuickActionKind.NewTodo:
                    CreateNestAtCursor(NestTypes.Todo);
                    break;
                case QuickActionKind.NewNote:
                    CreateNestAtCursor(NestTypes.Notes);
                    break;
                case QuickActionKind.Settings:
                    ShowSettings();
                    break;
            }
        }
    }
}
