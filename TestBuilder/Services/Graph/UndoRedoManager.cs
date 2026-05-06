using System;
using System.Collections.Generic;

namespace TestBuilder.Services.Graph
{
    public class UndoRedoManager
    {
        private readonly Stack<IGraphCommand> _undoStack = new();
        private readonly Stack<IGraphCommand> _redoStack = new();
        private const int MaxHistory = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public event Action? StateChanged;

        public void Execute(IGraphCommand command)
        {
            command.Execute();
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
            cmd.Undo();
            _redoStack.Push(cmd);
            StateChanged?.Invoke();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            var cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);
            StateChanged?.Invoke();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke();
        }

        private void TrimStack()
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < MaxHistory - 1; i++)
                _undoStack.Push(items[i]);
        }
    }
}
