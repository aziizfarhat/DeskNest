using System;
using System.Collections.Generic;

namespace DeskNest
{
    // Une action annulable : elle sait se rejouer dans les deux sens.
    public interface IUndoable
    {
        string Label { get; }
        void Undo();
        void Redo();
    }

    public sealed class DelegateUndo : IUndoable
    {
        public string Label { get; }
        private readonly Action _undo;
        private Action _redo;

        public DelegateUndo(string label, Action undo, Action redo)
        {
            Label = label;
            _undo = undo;
            _redo = redo;
        }

        public void Undo() => _undo();
        public void Redo() => _redo();

        // Fusionne deux modifications rapprochées de la même zone en une seule étape.
        public void ReplaceRedo(Action redo) => _redo = redo;
    }

    // Plusieurs actions forment une seule étape d'annulation (déplacement de plusieurs éléments, etc.).
    public sealed class CompositeUndo : IUndoable
    {
        public string Label { get; }
        private readonly List<IUndoable> _items;

        public CompositeUndo(string label, List<IUndoable> items)
        {
            Label = label;
            _items = items;
        }

        public void Undo()
        {
            for (int i = _items.Count - 1; i >= 0; i--) _items[i].Undo();
        }

        public void Redo()
        {
            foreach (var item in _items) item.Redo();
        }
    }

    // Pile d'annulation / rétablissement.
    public sealed class UndoStack
    {
        private const int MaxEntries = 100;
        private readonly List<IUndoable> _undo = new();
        private readonly List<IUndoable> _redo = new();
        private List<IUndoable>? _batch;
        private int _batchDepth;

        public event Action? Changed;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool IsBatching => _batchDepth > 0;
        public string? UndoLabel => _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null;
        public string? RedoLabel => _redo.Count > 0 ? _redo[_redo.Count - 1].Label : null;

        public void Push(IUndoable action)
        {
            if (_batchDepth > 0) { _batch!.Add(action); return; }
            _undo.Add(action);
            _redo.Clear();
            if (_undo.Count > MaxEntries) _undo.RemoveAt(0);
            Changed?.Invoke();
        }

        public void BeginBatch()
        {
            if (_batchDepth++ == 0) _batch = new List<IUndoable>();
        }

        public void EndBatch(string label)
        {
            if (_batchDepth == 0) return;
            if (--_batchDepth > 0) return;
            var items = _batch ?? new List<IUndoable>();
            _batch = null;
            if (items.Count == 0) return;
            Push(items.Count == 1 ? items[0] : new CompositeUndo(label, items));
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var action = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            action.Undo();
            _redo.Add(action);
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var action = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            action.Redo();
            _undo.Add(action);
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _batch = null;
            _batchDepth = 0;
            Changed?.Invoke();
        }
    }
}
