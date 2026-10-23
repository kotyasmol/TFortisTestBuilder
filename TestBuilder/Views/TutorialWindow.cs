using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Views;

internal enum TutorialStep
{
    Model, Profile, Start, Confirm, Result, Rename, Parameter, SaveCopy,
    New, Device, HideProfiles, AddStart, AddAction, AddDelay, AddEnd, ActionMessage, Help, CloseHelp,
    ConnectStart, ConnectSuccess, ConnectEnd, ConnectFailure, Name, Save, Run, ConfirmGraph, Wait, Done
}

/// <summary>Real views and commands, with a separate offline workspace and action-gated coach marks.</summary>
public sealed class TutorialWindow : Window
{
    private TutorialSession _session = new();
    internal TestViewModel Model => _session.Model;
    internal TutorialStep Step { get; private set; }
    private readonly StationDashboardView _station = new();
    private readonly TestView _editor = new();
    private readonly Grid _surface = new();
    private readonly Canvas _spotlight = new();
    private readonly Border[] _shades = Enumerable.Range(0, 4).Select(_ => new Border { Background = new SolidColorBrush(Color.Parse("#650D1828")) }).ToArray();
    private readonly Border _outline = new() { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(7), IsHitTestVisible = false };
    private readonly Border[] _portOutlines = Enumerable.Range(0, 2).Select(_ => new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(30), IsHitTestVisible = false, IsVisible = false }).ToArray();
    private readonly Button _workspace = new() { Content = "Открыть редактор", Name = "TutorialEditor" };
    private readonly Button _restart = new() { Content = "Начать заново", Name = "TutorialRestart" };
    private readonly Button _finish = new() { Content = "Закончить обучение", Name = "TutorialFinish" };
    private readonly TextBlock _counter = new() { FontSize = 13 };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _instruction = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _closing;
    private bool _disposed;
    private int _arrangedCount = -1;
    private bool _focusParameter;
    internal Rect HighlightBounds { get; private set; }
    internal Control? Target { get; private set; }

    public TutorialWindow()
    {
        Title = "Обучение TFortis TestBuilder"; Width = 1320; Height = 850; MinWidth = 1100; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, new DynamicResourceExtension("AppBackgroundBrush"));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 10) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = "Обучение", FontSize = 18, FontWeight = FontWeight.SemiBold });
        var mode = new TextBlock { Text = "Учебные профили · без оборудования", VerticalAlignment = VerticalAlignment.Center };
        mode.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextSecondaryBrush"));
        heading.Children.Add(mode); header.Children.Add(heading); Grid.SetColumn(_workspace, 1); header.Children.Add(_workspace);
        root.Children.Add(header);
        _surface.Children.Add(_station); _surface.Children.Add(_editor);
        Grid.SetRow(_surface, 1); root.Children.Add(_surface);
        foreach (var shade in _shades) _spotlight.Children.Add(shade);
        _outline.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PrimaryBrush"));
        _spotlight.Children.Add(_outline);
        foreach (var port in _portOutlines) { port.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PrimaryBrush")); _spotlight.Children.Add(port); }
        _surface.Children.Add(_spotlight);
        var coach = new Border { Padding = new Thickness(24, 16), BorderThickness = new Thickness(0, 1, 0, 0) };
        coach.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush"));
        coach.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        var coachLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220") };
        var text = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 24, 0) };
        _counter.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextSecondaryBrush"));
        text.Children.Add(_counter); text.Children.Add(_title); text.Children.Add(_instruction);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        coachLayout.Children.Add(text);
        var actions = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_restart); actions.Children.Add(_finish);
        Grid.SetColumn(actions, 1); coachLayout.Children.Add(actions); coach.Child = coachLayout;
        Grid.SetRow(coach, 2); root.Children.Add(coach); Content = root;
        _workspace.Click += (_, _) =>
        {
            if (Step != TutorialStep.Result || Model.IsTestRunning) return;
            Model.IsOperatorView = false; ChangeStep(TutorialStep.Rename);
        };
        _restart.Click += (_, _) => Restart();
        _finish.Click += (_, _) => Close();
        _timer.Tick += OnTick;
        Opened += (_, _) => { BindSession(); _timer.Start(); UpdateCoach(); };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void BindSession()
    {
        Model.TrainingDialogOwner = () => this;
        _station.DataContext = Model; _editor.DataContext = Model;
        _station.IsVisible = Model.IsOperatorView; _editor.IsVisible = !Model.IsOperatorView;
    }

    private void Restart()
    {
        if (Model.IsTestRunning || Model.SaveGraphCommand.IsRunning) return;
        foreach (var help in OwnedWindows.OfType<NodeHelpWindow>().ToArray()) help.Close();
        _session.Dispose(); _session = new TutorialSession(); _arrangedCount = -1;
        BindSession(); ChangeStep(TutorialStep.Model);
    }

    private void ChangeStep(TutorialStep step)
    {
        Step = step;
        _focusParameter = step is TutorialStep.Parameter or TutorialStep.ActionMessage or TutorialStep.Help;
        if (step == TutorialStep.ConnectStart) Graph?.FitToScreen();
        BindSession();
        if (step == TutorialStep.Done) TutorialSession.RecordCompletion(AppSettings.Instance);
        UpdateCoach();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || _closing) return;
        _restart.IsEnabled = !Model.IsTestRunning && !Model.SaveGraphCommand.IsRunning;
        _workspace.IsEnabled = Step == TutorialStep.Result && !Model.IsTestRunning;
        // Each transition requires the real UI command or edited value; there is no Next shortcut.
        var advanced = Step switch
        {
            TutorialStep.Model => Model.StationProfileGroups.Any(g => g.IsExpanded),
            TutorialStep.Profile => Model.HasStationProfile,
            TutorialStep.Start => Model.Station.IsAwaitingAction,
            TutorialStep.Confirm => Model.Station.HasResult && Model.Station.IsSuccessful && !Model.IsTestRunning,
            TutorialStep.Rename => !string.IsNullOrWhiteSpace(Model.ProfileDescription) && Model.ProfileDescription != "Знакомство с программой",
            TutorialStep.Parameter => Model.Nodes.OfType<DelayNodeViewModel>().Any(d => d.Milliseconds == 500),
            TutorialStep.SaveCopy => !Model.SaveGraphCommand.IsRunning && Model.Profiles.Count == 2 && Model.SelectedProfile?.FilePath != _session.OriginalPath,
            TutorialStep.New => Model.Nodes.Count == 0,
            TutorialStep.Device => !string.IsNullOrWhiteSpace(Model.DeviceName),
            TutorialStep.HideProfiles => Model.IsProfilesCollapsed,
            TutorialStep.AddStart => Model.Nodes.OfType<StartNodeViewModel>().Count() == 1,
            TutorialStep.AddAction => Model.Nodes.OfType<OperatorActionNodeViewModel>().Count() == 1,
            TutorialStep.AddDelay => Model.Nodes.OfType<DelayNodeViewModel>().Count() == 1,
            TutorialStep.AddEnd => Model.Nodes.OfType<EndNodeViewModel>().Count() == 1,
            TutorialStep.ActionMessage => Model.Nodes.OfType<OperatorActionNodeViewModel>().Any(n => !string.IsNullOrWhiteSpace(n.Message)) && FindTarget()?.IsKeyboardFocusWithin != true,
            TutorialStep.Help => OwnedWindows.OfType<NodeHelpWindow>().Any(),
            TutorialStep.CloseHelp => !OwnedWindows.OfType<NodeHelpWindow>().Any(),
            TutorialStep.ConnectStart => HasLink<StartNodeViewModel, OperatorActionNodeViewModel>(),
            TutorialStep.ConnectSuccess => HasActionLink(false),
            TutorialStep.ConnectEnd => HasLink<DelayNodeViewModel, EndNodeViewModel>(),
            TutorialStep.ConnectFailure => HasActionLink(true),
            TutorialStep.Name => !string.IsNullOrWhiteSpace(Model.ProfileDescription),
            TutorialStep.Save => !Model.SaveGraphCommand.IsRunning && Model.Profiles.Count == 3 && Model.SelectedProfile != null,
            TutorialStep.Run => OwnedWindows.OfType<OperatorActionDialog>().Any(),
            TutorialStep.ConfirmGraph => Model.Station.HasResult && !Model.IsTestRunning,
            TutorialStep.Wait => !Model.IsTestRunning && Model.Station.HasResult,
            _ => false
        };
        if (advanced) ChangeStep(Step + 1);
        ArrangeExerciseNodes();
        FocusParameter();
        UpdateHighlight();
    }

    private bool HasActionLink(bool failure) => Model.Connections.Any(c => c.Source.Parent is OperatorActionNodeViewModel action &&
        ReferenceEquals(c.Source, failure ? action.FalseOut : action.TrueOut) &&
        (failure ? c.Target.Parent is EndNodeViewModel : c.Target.Parent is DelayNodeViewModel));

    private bool HasLink<TFrom, TTo>() => Model.Connections.Any(c => c.Source.Parent is TFrom && c.Target.Parent is TTo);

    private void UpdateCoach()
    {
        _workspace.IsEnabled = Step == TutorialStep.Result;
        _workspace.IsVisible = Model.IsOperatorView;
        _counter.Text = Step < TutorialStep.Rename ? "Пульт · " + ((int)Step + 1) + " из 5" : Step == TutorialStep.Done ? "Обучение завершено" : "Редактор · " + ((int)Step - 4) + " из " + ((int)TutorialStep.Done - (int)TutorialStep.Rename);
        var copy = Step switch
        {
            TutorialStep.Model => ("Выберите устройство", "Нажмите «Учебное устройство» слева. Здесь сгруппированы программы проверки одной модели."),
            TutorialStep.Profile => ("Откройте программу проверки", "Нажмите «Знакомство с программой». Справа появятся её этапы."),
            TutorialStep.Start => ("Запустите проверку", "Нажмите «Начать». Учебная программа работает без стенда."),
            TutorialStep.Confirm => ("Выполните задание оператора", "Прочитайте задание и нажмите «Готово · продолжить». Затем дождитесь результата."),
            TutorialStep.Result => ("Посмотрите результат", "Проверка завершена. На пульте видны результат и затраченное время. Нажмите «Открыть редактор», чтобы научиться менять профили, или закончите обучение."),
            TutorialStep.Rename => ("Дайте копии другое имя", "В правом поле названия введите «Мой учебный профиль» или своё название и нажмите Enter. При сохранении с новым именем исходный профиль останется прежним."),
            TutorialStep.Parameter => ("Измените параметр ноды", "В ноде «Задержка» замените значение «Мс» на 500. Это пауза в половину секунды."),
            TutorialStep.SaveCopy => ("Сохраните копию", "Нажмите «Сохранить». В учебном списке появится второй профиль с вашим названием."),
            TutorialStep.New => ("Создайте свой граф", "Нажмите «Новый» в левом меню. Соберём проверку с заданием оператору и двумя исходами."),
            TutorialStep.Device => ("Укажите новое устройство", "В поле «Устройство» слева введите «Учебная плата» и нажмите Enter. Существующие устройства можно выбрать из подсказок; для нового достаточно ввести название."),
            TutorialStep.HideProfiles => ("Освободите место для графа", "Нажмите кнопку «Скрыть профили» слева от поля устройства. Эта же кнопка возвращает список, когда он понадобится."),
            TutorialStep.AddStart => ("Добавьте старт", "Перетащите «Старт» из палитры справа на свободное место графа. От этой ноды начинается выполнение."),
            TutorialStep.AddAction => ("Добавьте задание оператору", "Перетащите «Действие оператора» на граф. Эта нода приостанавливает проверку, пока человек не подтвердит действие или не отменит его."),
            TutorialStep.AddDelay => ("Добавьте задержку", "Перетащите «Задержка» из палитры на граф. Эта нода задаёт время ожидания."),
            TutorialStep.AddEnd => ("Добавьте конец", "Перетащите «Конец» на граф. Он завершает выполнение программы."),
            TutorialStep.ActionMessage => ("Напишите задание", "В большом поле ноды «Действие оператора» введите «Проверьте индикатор» и нажмите Tab. При запуске этот текст увидит оператор. Поле «Переменная» оставьте пустым."),
            TutorialStep.Help => ("Откройте справку ноды", "Нажмите «i» в заголовке ноды «Действие оператора». В справке описаны её параметры и выходы."),
            TutorialStep.CloseHelp => ("Посмотрите описание выходов", "В открывшейся справке прочитайте, что означают «Продолжить» и «Отмена», затем нажмите «Закрыть» в окне справки."),
            TutorialStep.ConnectStart => ("Соедините старт и задание", "Зажмите выделенный кружок «Выход» у старта и перетащите линию к выделенному «Вход» ноды «Действие оператора». Линия задаёт следующий шаг."),
            TutorialStep.ConnectSuccess => ("Соедините успешный исход", "Перетащите линию от «Продолжить» ноды «Действие оператора» к «Вход» задержки. После подтверждения оператора выполнится задержка."),
            TutorialStep.ConnectEnd => ("Соедините задержку и конец", "Перетащите линию между выделенными кружками: от «Выход» задержки к «Вход» конца."),
            TutorialStep.ConnectFailure => ("Соедините отмену", "Перетащите линию от «Отмена» ноды «Действие оператора» к «Вход» конца. При отмене граф завершится без задержки. У одного входа может быть несколько линий."),
            TutorialStep.Name => ("Назовите новую проверку", "В правом поле введите «Пробная проверка» и нажмите Enter. Полное имя составится из устройства слева и названия проверки справа."),
            TutorialStep.Save => ("Сохраните новый профиль", "Нажмите «Сохранить». В обычной работе файл появится в папке профилей, выбранной в настройках. Учебные файлы удалятся при закрытии обучения."),
            TutorialStep.Run => ("Проверьте свой граф", "Нажмите «Запустить». Появится окно с заданием, которое вы написали."),
            TutorialStep.ConfirmGraph => ("Подтвердите своё задание", "В окне задания нажмите «Продолжить». Граф перейдёт к задержке по соединённому вами выходу. Кнопка «Отмена» направляет выполнение сразу к концу."),
            TutorialStep.Wait => ("Выполняется проверка", "Дождитесь окончания задержки. Результаты шагов появятся в журнале под графом."),
            _ => ("Вы собрали и запустили профиль", "Теперь вы умеете выбирать проверку, создавать устройство, менять параметры, сохранять копию, пользоваться справкой и соединять разные исходы. Повторить обучение можно в настройках. Подробные действия и справка по каждой ноде находятся в «Полной инструкции».")
        };
        _title.Text = copy.Item1; _instruction.Text = copy.Item2;
        _finish.Content = Step == TutorialStep.Done ? "Завершить" : "Закончить обучение";
        UpdateHighlight();
    }

    private NodifyEditor? Graph => _editor.GetVisualDescendants().OfType<NodifyEditor>().FirstOrDefault();
    private Button? ButtonIn(Control root, string name) => root.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
        b.IsEffectivelyVisible && (Equals(b.Content, name) || AutomationProperties.GetName(b) == name));
    private Control? FindTarget() => Step switch
    {
        TutorialStep.Model => ButtonIn(_station, "Учебное устройство"),
        TutorialStep.Profile => ButtonIn(_station, "Знакомство с программой"),
        TutorialStep.Start => ButtonIn(_station, "Начать"),
        TutorialStep.Confirm => Model.Station.IsAwaitingAction ? ButtonIn(_station, "Готово · продолжить") : null,
        TutorialStep.Result => null, // result and the workspace button remain readable and clickable
        TutorialStep.Rename or TutorialStep.Name => _editor.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault(c => c.Name == "ProfileNameInput"),
        TutorialStep.Device => _editor.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault(c => c.Name == "DeviceNameInput"),
        TutorialStep.HideProfiles => _editor.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, Model.ToggleProfilesCommand)),
        TutorialStep.ActionMessage => Graph?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c => c.DataContext is OperatorActionNodeViewModel),
        TutorialStep.Help => Graph?.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => c.Classes.Contains("nodeHelpTrigger") && c.DataContext is OperatorActionNodeViewModel),
        TutorialStep.Parameter => Graph?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c => c.DataContext is DelayNodeViewModel),
        TutorialStep.SaveCopy or TutorialStep.Save => ButtonIn(_editor, "Сохранить профиль"),
        TutorialStep.New => ButtonIn(_editor, "Новый"),
        TutorialStep.AddStart or TutorialStep.AddAction or TutorialStep.AddDelay or TutorialStep.AddEnd => Graph?.Parent as Control,
        TutorialStep.ConnectStart or TutorialStep.ConnectSuccess or TutorialStep.ConnectEnd or TutorialStep.ConnectFailure => Graph,
        TutorialStep.Run => ButtonIn(_editor, "Запустить тест"),
        _ => null
    };

    internal void UpdateHighlight()
    {
        Target = FindTarget();
        var point = Target?.TranslatePoint(default, _surface);
        _spotlight.IsVisible = point != null && Target!.Bounds.Width > 0;
        if (!_spotlight.IsVisible) { HighlightBounds = default; return; }
        var target = Target!;
        var corner = target.TranslatePoint(new Point(target.Bounds.Width, target.Bounds.Height), _surface)!.Value;
        var targetRect = new Rect(point!.Value, corner).Inflate(5);
        var area = new Rect(_surface.Bounds.Size);
        var r = targetRect.Intersect(area); HighlightBounds = r;
        Place(_shades[0], new Rect(0, 0, area.Width, r.Top));
        Place(_shades[1], new Rect(0, r.Bottom, area.Width, Math.Max(0, area.Height - r.Bottom)));
        Place(_shades[2], new Rect(0, r.Top, r.Left, r.Height));
        Place(_shades[3], new Rect(r.Right, r.Top, Math.Max(0, area.Width - r.Right), r.Height));
        Place(_outline, r);
        foreach (var p in _portOutlines) p.IsVisible = false;
        if (Step is TutorialStep.ConnectStart or TutorialStep.ConnectSuccess or TutorialStep.ConnectEnd or TutorialStep.ConnectFailure && Graph is { } graph)
        {
            var action = Model.Nodes.OfType<OperatorActionNodeViewModel>().FirstOrDefault();
            var ports = Step switch
            {
                TutorialStep.ConnectStart => new[] { Model.Nodes.OfType<StartNodeViewModel>().FirstOrDefault()?.Output.FirstOrDefault(), action?.In },
                TutorialStep.ConnectSuccess => new[] { action?.TrueOut, Model.Nodes.OfType<DelayNodeViewModel>().FirstOrDefault()?.Input.FirstOrDefault() },
                TutorialStep.ConnectEnd => new[] { Model.Nodes.OfType<DelayNodeViewModel>().FirstOrDefault()?.Output.FirstOrDefault(), Model.Nodes.OfType<EndNodeViewModel>().FirstOrDefault()?.Input.FirstOrDefault() },
                _ => new[] { action?.FalseOut, Model.Nodes.OfType<EndNodeViewModel>().FirstOrDefault()?.Input.FirstOrDefault() }
            };
            for (var i = 0; i < ports.Length; i++)
            {
                var c = graph.GetVisualDescendants().OfType<Connector>().FirstOrDefault(c => ReferenceEquals(c.DataContext, ports[i]));
                var thumb = c?.GetVisualDescendants().OfType<Control>().FirstOrDefault(v => v.Name == "PART_Connector") ?? c;
                if (thumb?.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), _surface) is { } center)
                { _portOutlines[i].IsVisible = true; Place(_portOutlines[i], new Rect(center.X - 12, center.Y - 12, 24, 24)); }
            }
        }
    }

    private static void Place(Control c, Rect r)
    {
        Canvas.SetLeft(c, r.X); Canvas.SetTop(c, r.Y); c.Width = Math.Max(0, r.Width); c.Height = Math.Max(0, r.Height);
    }

    private void ArrangeExerciseNodes()
    {
        if (Step < TutorialStep.AddStart || Step > TutorialStep.ConnectFailure || Graph is not { } editor || _arrangedCount == Model.Nodes.Count) return;
        var containers = editor.GetVisualDescendants().OfType<ItemContainer>().Where(c => c.DataContext is NodeViewModel).ToArray();
        if (containers.Length != Model.Nodes.Count || containers.Any(c => c.Bounds.Width <= 0)) return;
        // Use measured template sizes, never a guessed uniform step. Only the exercise is arranged.
        var sizes = containers.ToDictionary(c => (NodeViewModel)c.DataContext!, c => c.Bounds.Size);
        var column = sizes.Where(p => p.Key is StartNodeViewModel or DelayNodeViewModel).Select(p => p.Value.Width).DefaultIfEmpty(220).Max() + 96;
        var row = sizes.Where(p => p.Key is StartNodeViewModel or OperatorActionNodeViewModel).Select(p => p.Value.Height).DefaultIfEmpty(160).Max() + 96;
        foreach (var node in Model.Nodes)
            node.Location = node switch
            {
                StartNodeViewModel => new Point(40, 40),
                OperatorActionNodeViewModel => new Point(40 + column, 40),
                DelayNodeViewModel => new Point(40, 40 + row),
                _ => new Point(40 + column, 40 + row)
            };
        _arrangedCount = Model.Nodes.Count;
        Dispatcher.UIThread.Post(() => { if (!_disposed && !_closing) editor.FitToScreen(); }, DispatcherPriority.Background);
    }

    private void FocusParameter()
    {
        if (!_focusParameter || Graph is not { } graph || !graph.IsEffectivelyVisible) return;
        var container = graph.GetVisualDescendants().OfType<ItemContainer>().FirstOrDefault(c => Step == TutorialStep.Parameter ? c.DataContext is DelayNodeViewModel : c.DataContext is OperatorActionNodeViewModel);
        if (container?.DataContext is not NodeViewModel node || container.Bounds.Width <= 0) return;
        graph.ViewportZoom = 1;
        graph.ViewportLocation = new Point(node.Location.X - (graph.Bounds.Width - container.Bounds.Width) / 2,
            node.Location.Y - (graph.Bounds.Height - container.Bounds.Height) / 2);
        _focusParameter = false;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Model.IsTestRunning || Model.SaveGraphCommand.IsRunning)
        {
            e.Cancel = true;
            if (!_closing) _ = StopAndCloseAsync();
        }
        base.OnClosing(e);
    }

    private async System.Threading.Tasks.Task StopAndCloseAsync()
    {
        _closing = true; _surface.IsEnabled = false; _restart.IsEnabled = false;
        Model.StopTestCommand.Execute(null);
        try
        {
            if (Model.RunGraphCommand.ExecutionTask is { } run) await run;
            if (Model.SaveGraphCommand.ExecutionTask is { } save) await save;
        }
        finally { _closing = false; Close(); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _disposed = true; _timer.Stop(); _timer.Tick -= OnTick;
        _editor.Dispose(); _editor.DataContext = null; _station.DataContext = null;
        _session.Dispose(); base.OnClosed(e);
    }
}
