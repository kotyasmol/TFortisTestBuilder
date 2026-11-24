using System;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    // A failed UI conversion leaves the previous typed value in the model.
    // Check the editors before serializing or executing that value.
    internal event Func<string?>? ValidateEditorInputs;

    internal bool CheckEditorInputs()
    {
        if (ValidateEditorInputs == null) return true;
        foreach (Func<string?> validate in ValidateEditorInputs.GetInvocationList())
        {
            if (validate() is not { } error) continue;
            StatusMessage = error;
            return false;
        }
        return true;
    }
}
