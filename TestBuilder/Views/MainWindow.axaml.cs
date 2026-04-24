using Avalonia.Controls;
using Avalonia.Threading;
using TestBuilder.ViewModels;

namespace TestBuilder.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Устанавливаем DataContext после того как все контролы созданы
            DataContextChanged += (_, _) => AssignDataContexts();

            // На случай если DataContext уже установлен до подписки
            Dispatcher.UIThread.Post(AssignDataContexts);
        }

        private void AssignDataContexts()
        {
            if (DataContext is not MainWindowViewModel vm) return;

            TestViewControl.DataContext = vm.TestVM;
            ModbusViewControl.DataContext = vm.ModbusVM;
            SettingsViewControl.DataContext = vm.SettingsVM;
        }
    }
}