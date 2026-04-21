using Avalonia.Controls;
using TestBuilder.ViewModels;

namespace TestBuilder.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Устанавливаем DataContext для вложенных view напрямую из кода,
            // а не через {Binding TestVM} в XAML.
            //
            // Проблема: при переключении вкладок Avalonia TabControl пересоздаёт
            // контент вкладки. В момент пересоздания Avalonia временно устанавливает
            // родительский DataContext (MainWindowViewModel) на дочерний view,
            // и только потом применяет Binding. За это время NodifyEditor успевает
            // получить неправильный DataContext и теряет привязку к PendingConnection.
            // После этого соединения между нодами перестают создаваться.
            //
            // Решение: устанавливать DataContext один раз явно из кода — тогда
            // он никогда не меняется при переключении вкладок.
            if (DataContext is MainWindowViewModel vm)
            {
                TestViewControl.DataContext = vm.TestVM;
                ModbusViewControl.DataContext = vm.ModbusVM;
            }
        }
    }
}