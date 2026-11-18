using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskNest
{
    // Règles d'ordonnancement des tâches, isolées de l'interface pour être testables.
    // L'ordre manuscrit reste dans NestData.Tasks : « déplacer les tâches cochées en
    // bas » est un ordre d'affichage, pas une mutation, ce qui restaure leur place
    // d'origine lorsqu'on décoche une tâche.
    public static class TodoOrder
    {
        public static List<TodoTask> Sort(IEnumerable<TodoTask>? tasks, bool checkedToBottom)
        {
            var list = tasks?.Where(t => t != null).ToList() ?? new List<TodoTask>();
            if (!checkedToBottom) return list;
            return list.Where(t => !t.Done).Concat(list.Where(t => t.Done)).ToList();
        }

        // Entrée (touche principale ou pavé numérique) et bouton « + » passent par
        // cette même méthode : une saisie vide ou blanche ne crée aucune tâche.
        public static bool TryCreate(string? raw, out TodoTask? task)
        {
            var text = raw?.Trim() ?? "";
            if (text.Length == 0)
            {
                task = null;
                return false;
            }
            task = new TodoTask { Text = text };
            return true;
        }

        // Validation d'une édition en place. Une saisie vide est refusée : la
        // tâche garde son texte précédent et l'interface reste en mode édition.
        public static bool CommitEdit(TodoTask task, string? raw)
        {
            var text = raw?.Trim() ?? "";
            if (text.Length == 0) return false;
            task.Text = text;
            return true;
        }

        public static void Insert(List<TodoTask> tasks, TodoTask task, bool addAtTop)
        {
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            if (task == null) throw new ArgumentNullException(nameof(task));
            tasks.Insert(addAtTop ? 0 : tasks.Count, task);
        }

        // Déplace une tâche dans l'ordre manuscrit à partir de sa position d'affichage.
        // « before » / « after » sont les voisins visuels après le dépôt.
        public static void Move(List<TodoTask> tasks, TodoTask task, TodoTask? before, TodoTask? after)
        {
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            if (task == null) throw new ArgumentNullException(nameof(task));
            tasks.Remove(task);
            int at;
            if (after != null)
            {
                at = tasks.IndexOf(after);
                if (at < 0) at = tasks.Count;
            }
            else if (before != null)
            {
                at = tasks.IndexOf(before);
                at = at < 0 ? tasks.Count : at + 1;
            }
            else at = tasks.Count;
            tasks.Insert(Math.Clamp(at, 0, tasks.Count), task);
        }

        public static string CountText(int done, int total) => $"{done}/{total}";
    }
}
