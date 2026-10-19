using System;
using System.IO;
using System.Linq;
using TestBuilder.Services;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private readonly string? _trainingDirectory;
    public bool IsTraining => _trainingDirectory != null;
    internal Func<Avalonia.Controls.Window?>? TrainingDialogOwner { get; set; }
    private string ProfilesFolder => _trainingDirectory ?? AppSettings.Instance.GraphsFolder;

    private bool IsAllowedProfilePath(string path) => !IsTraining ||
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(_trainingDirectory!), StringComparison.Ordinal);

    // This is an isolated exercise, even if a graph is changed through shortcuts
    // or a future UI exposes additional palette entries. Never run external I/O.
    private bool IsSafeTrainingGraph() => RootGraph.Hardware == null && RootGraph.Nodes.All(n =>
        n is StartNodeViewModel or EndNodeViewModel or DelayNodeViewModel ||
        n is OperatorActionNodeViewModel { InputVariable.Length: 0 });
}
