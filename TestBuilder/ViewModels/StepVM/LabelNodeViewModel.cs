using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class LabelNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private string text = "Этап";

        public LabelNodeViewModel()
        {
            Title = "Label";
        }
    }
}