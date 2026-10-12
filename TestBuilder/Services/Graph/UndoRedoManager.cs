using System;
using System.Collections.Generic;

namespace TestBuilder.Services.Graph
{
    public class UndoRedoManager
    {
        private readonly Stack<IGraphCommand> _undoStack = new();
        private readonly Stack<IGraphCommand> _redoStack = new();
        public const int MaxHistory = 50;
        public bool IsApplying { get; private set; }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public event Action? StateChanged;

        public void Execute(IGraphCommand command)
        {
            Apply(command.Execute);
            Record(command);
        }

        public void Record(IGraphCommand command)
        {
            if (IsApplying) return;
            _undoStack.Push(command);
            _redoStack.Clear();

            if (_undoStack.Count > MaxHistory)
                TrimStack();

            StateChanged?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            var cmd = _undoStack.Pop();
            Apply(cmd.Undo);
            _redoStack.Push(cmd);
            StateChanged?.Invoke();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var cmd = _redoStack.Pop();
            Apply(cmd.Execute);
            _undoStack.Push(cmd);
            StateChanged?.Invoke();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke();
        }

        private void Apply(Action action)
        {
            IsApplying = true;
            try { action(); }
            finally { IsApplying = false; }
        }

        private void TrimStack()
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = MaxHistory - 1; i >= 0; i--)
                _undoStack.Push(items[i]);
        }
    }
}
