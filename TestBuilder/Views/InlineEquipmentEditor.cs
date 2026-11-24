using System;
using Avalonia.Controls;

namespace TestBuilder.Views;

internal abstract class InlineEquipmentEditor<T> : UserControl where T : class
{
    public event Action<T?>? Completed;
    public void Cancel() => Complete(null);
    protected void Complete(T? result) => Completed?.Invoke(result);
}
