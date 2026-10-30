using Avalonia.Controls;
namespace TestBuilder.Views;
public partial class LabelPrintingView : UserControl
{
    private bool? _wide;
    public LabelPrintingView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var wide = Bounds.Width >= 1040;
            if (_wide == wide) return;
            _wide = wide;
            LabelDetailsGrid.ColumnDefinitions = new ColumnDefinitions(wide ? "*,24,400" : "*");
            LabelDetailsGrid.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,16,Auto");
            Grid.SetColumn(PreviewPanel, wide ? 2 : 0);
            Grid.SetRow(PreviewPanel, wide ? 0 : 2);
        };
    }
}
