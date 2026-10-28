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
            SelfTestPageViewControl.DataContext = vm.SelfTestPageVM;
            NetworkSetupViewControl.DataContext = vm.NetworkSetupVM;
            ShellSettingsViewControl.DataContext = vm.SettingsVM;
        }

        private async void OnOpenHardware(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (!vm.TestVM.CanEditHardware) { vm.TestVM.TestingLogger.Warning("Остановите тест и отключите стенд для редактирования оборудования."); return; }
            await new HardwareEditorWindow(vm.TestVM).ShowDialog(this);
        }
        private async void OnOpenBoard(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                if (vm.IsEngineerWorkspace && vm.TestVM.CanEditGraph)
                    await new BoardWindow(vm.TestVM, true).ShowDialog(this);
                else new BoardWindow(vm.TestVM, false).Show(this);
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (TestViewControl is System.IDisposable testView)
                testView.Dispose();

            if (DataContext is System.IDisposable disposable)
                disposable.Dispose();

            base.OnClosed(e);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && (vm.TestVM.IsTestRunning || vm.TestVM.HasRunningQuickActions))
            {
                e.Cancel = true;
                vm.TestVM.CancelQuickActions();
                vm.TestVM.StopTestCommand.Execute(null);
            }
            base.OnClosing(e);
        }
    }
}
