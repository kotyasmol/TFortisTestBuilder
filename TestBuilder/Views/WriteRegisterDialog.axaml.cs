using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Views;

public partial class WriteRegisterDialog : Window
{
    private readonly SlaveModelBase _slave;
    private readonly RegisterItem _register;
    private readonly System.Func<bool> _canWrite;

    public WriteRegisterDialog(SlaveModelBase slave, RegisterItem register, System.Func<bool>? canWrite = null)
    {
        InitializeComponent();

        _canWrite = canWrite ?? (() => true);
        _slave = slave;
        _register = register;

        SlaveIdBox.Text = slave.SlaveId.ToString();
        AddressBox.Text = register.Address.ToString();
        ValueBox.Text = register.Value.ToString();

        SendButton.Click += OnSendClick;
        CancelButton.Click += OnCancelClick;
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (!_canWrite()) { ResultText.Text = "Ручная запись недоступна во время проверки или подключения."; return; }
        if (!ushort.TryParse(ValueBox.Text, out ushort value))
        {
            ResultText.Text = "Ошибка: неверное значение";
            ResultText.Foreground = Avalonia.Media.Brushes.Red;
            return;
        }

        if (!byte.TryParse(SlaveIdBox.Text, out byte slaveId))
        {
            ResultText.Text = "Ошибка: неверный Slave ID";
            ResultText.Foreground = Avalonia.Media.Brushes.Red;
            return;
        }

        try
        {
            await _slave.Modbus.WriteAreaAsync(slaveId, (ushort)_register.Address, value, _register.Area);
            await _slave.PollAsync();
            ResultText.Text = "Запись выполнена успешно";
            ResultText.Foreground = Avalonia.Media.Brushes.Green;
        }
        catch
        {
            ResultText.Text = "Ошибка записи";
            ResultText.Foreground = Avalonia.Media.Brushes.Red;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}