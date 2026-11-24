using Avalonia.Controls;
using Avalonia.Threading;
using TestBuilder.ViewModels;

namespace TestBuilder.Views
{
    public partial class MainWindow : Window
    {
        private WorkspacePersistence? _workspacePersistence;
        private HardwareEditorView? _hardwareEditor;
        private EquipmentLibraryView? _equipmentLibrary;
        internal System.Func<TestViewModel, HardwareEditorView> HardwareEditorFactory { get; set; } = vm => new HardwareEditorView(vm);
        internal System.Func<EquipmentLibraryView> EquipmentLibraryFactory { get; set; } = () => new EquipmentLibraryView();

        internal void EnableWorkspacePersistence(Services.AppSettings settings, System.Action? save = null)
        {
            AssignDataContexts();
            _workspacePersistence?.Dispose();
            _workspacePersistence = new WorkspacePersistence(this, settings, save ?? settings.SaveChecked);
        }
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

        internal void OpenProfileHardware(TestViewModel source)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (!ReferenceEquals(source, vm.TestVM) || !vm.IsEngineerWorkspace) return;
            if (_hardwareEditor != null) { vm.IsHardwareOpen = true; return; }
            if (!vm.IsWorkspaceOpen) return;
            if (!vm.TestVM.CanEditHardware) { vm.TestVM.TestingLogger.Warning("Остановите тест и отключите стенд для редактирования оборудования."); return; }
            _hardwareEditor = HardwareEditorFactory(vm.TestVM);
            _hardwareEditor.Finished += () =>
            {
                HardwareWorkspaceHost.Content = null;
                _hardwareEditor = null;
                vm.IsHardwareOpen = false;
            };
            HardwareWorkspaceHost.Content = _hardwareEditor;
            vm.IsHardwareOpen = true;
        }
        private void OnCancelHardware(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _hardwareEditor?.Finish();
        private void OnOpenEquipmentLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel { IsEngineerWorkspace: true } vm) return;
            if (_equipmentLibrary != null) { vm.IsEquipmentLibraryOpen = true; return; }
            if (!vm.IsWorkspaceOpen) return;
            _equipmentLibrary = EquipmentLibraryFactory();
            _equipmentLibrary.Finished += () =>
            { EquipmentLibraryWorkspaceHost.Content = null; _equipmentLibrary = null; vm.IsEquipmentLibraryOpen = false; };
            EquipmentLibraryWorkspaceHost.Content = _equipmentLibrary; vm.IsEquipmentLibraryOpen = true;
        }
        private void OnCloseEquipmentLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _equipmentLibrary?.Finish();
        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            if (DataContext is MainWindowViewModel vm) vm.TestVM.SetPingMonitoringActive(true);
        }
        protected override void OnClosed(System.EventArgs e)
        {
            _workspacePersistence?.Dispose();
            if (TestViewControl is System.IDisposable testView)
                testView.Dispose();

            if (DataContext is System.IDisposable disposable)
                disposable.Dispose();

            base.OnClosed(e);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (DataContext is MainWindowViewModel { NetworkSetupVM.IsBusy: true }) e.Cancel = true;
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
