using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RhythmPomodoro.Controls;

namespace RhythmPomodoro;

public partial class MainWindow : Window
{
    private sealed record BreakEditTag(BreakInterval Break, bool IsStart);

    private readonly List<BreakInterval> _breaks = new();
    private readonly Dictionary<string, CyclePreset> _presets = new();
    private readonly List<(BreakEditTag Tag, TextBox Box)> _breakEditorBoxes = new();
    private readonly DispatcherTimer _timer;

    private List<CycleSegment> _schedule = new();
    private int _cycleMinutes = 50;
    private int _cycleCount = 4;
    private double _totalMs;
    private double _elapsedMs;
    private bool _running;
    private bool _completed;
    private bool _settingsOpen;
    private bool _updatingUi;
    private bool _loaded;
    private long _lastTick;
    private int _lastRenderedSecond = -1;
    private HwndSource? _hwndSource;

    private Brush _redBrush = Brushes.IndianRed;
    private Brush _redDeepBrush = Brushes.DarkRed;
    private Brush _redSoftBrush = Brushes.LightCoral;
    private Brush _redFaintBrush = Brushes.MistyRose;
    private Brush _greenBrush = Brushes.SeaGreen;
    private Brush _greenDeepBrush = Brushes.DarkGreen;
    private Brush _greenSoftBrush = Brushes.PaleGreen;
    private Brush _greenFaintBrush = Brushes.Honeydew;
    private Brush _goldBrush = Brushes.Goldenrod;
    private Brush _inkBrush = Brushes.Black;
    private Brush _inkSoftBrush = Brushes.DimGray;
    private Brush _mutedBrush = Brushes.Gray;
    private Brush _lineBrush = Brushes.LightGray;
    private Brush _cardBorderBrush = Brushes.LightGray;

    public MainWindow()
    {
        InitializeComponent();

        _redBrush = (Brush)FindResource("RedBrush");
        _redDeepBrush = (Brush)FindResource("RedDeepBrush");
        _redSoftBrush = (Brush)FindResource("RedSoftBrush");
        _redFaintBrush = (Brush)FindResource("RedFaintBrush");
        _greenBrush = (Brush)FindResource("GreenBrush");
        _greenDeepBrush = (Brush)FindResource("GreenDeepBrush");
        _greenSoftBrush = (Brush)FindResource("GreenSoftBrush");
        _greenFaintBrush = (Brush)FindResource("GreenFaintBrush");
        _goldBrush = (Brush)FindResource("GoldBrush");
        _inkBrush = (Brush)FindResource("InkBrush");
        _inkSoftBrush = (Brush)FindResource("InkSoftBrush");
        _mutedBrush = (Brush)FindResource("MutedBrush");
        _lineBrush = (Brush)FindResource("LineBrush");
        _cardBorderBrush = (Brush)FindResource("CardBorderBrush");

        _presets["classic"] = new CyclePreset("classic", "经典 25/5", 30, 4, (25, 30));
        _presets["double"] = new CyclePreset("double", "双段专注", 50, 4, (25, 30), (45, 50));
        _presets["deep"] = new CyclePreset("deep", "深度 50/10", 60, 3, (50, 60));

        _breaks.Add(new BreakInterval(25, 30));
        _breaks.Add(new BreakInterval(45, 50));

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += Timer_Tick;

        Timeline.PreviewChanged += Timeline_PreviewChanged;
        Timeline.Edited += Timeline_Edited;

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _hwndSource?.RemoveHook(WndProc);
        };

        RebuildSchedule();
        UpdateAll();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
        UpdateAll();
        UpdateSettingsLayout();
        UpdateMaximizeVisuals();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        try
        {
            var preference = 2;
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Rounded corners are a Windows 11 nicety only.
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBottomBar();
        UpdateSettingsLayout();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        UpdateMaximizeVisuals();
        UpdateBottomBar();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_running)
        {
            return;
        }

        AdvanceElapsed();
        UpdateTimerVisuals();

        if (_settingsOpen && _lastRenderedSecond >= 0)
        {
            UpdateSettingsStatus();
        }
    }

    private void AdvanceElapsed()
    {
        if (!_running)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var delta = (now - _lastTick) * 1000d / Stopwatch.Frequency;
        _lastTick = now;

        if (delta <= 0)
        {
            return;
        }

        _elapsedMs += delta;
        if (_elapsedMs >= _totalMs - 0.5)
        {
            _elapsedMs = _totalMs;
            _completed = true;
            _running = false;
        }
    }

    private double CycleMilliseconds => _cycleMinutes * 60_000d;

    private double LocalMinutes
    {
        get
        {
            if (_completed)
            {
                return _cycleMinutes;
            }

            var cycleMs = CycleMilliseconds;
            return cycleMs <= 0 ? 0 : (_elapsedMs % cycleMs) / 60_000d;
        }
    }

    private int CurrentCycleIndex
    {
        get
        {
            if (_completed)
            {
                return _cycleCount;
            }

            var cycleMs = CycleMilliseconds;
            if (cycleMs <= 0)
            {
                return 1;
            }

            return Math.Clamp((int)Math.Floor(_elapsedMs / cycleMs) + 1, 1, _cycleCount);
        }
    }

    private CycleSegment? CurrentSegment
    {
        get
        {
            if (_completed)
            {
                return null;
            }

            return ScheduleBuilder.FindSegment(_schedule, LocalMinutes);
        }
    }

    private void RebuildSchedule()
    {
        _schedule = ScheduleBuilder.Build(_cycleMinutes, _breaks);
        _totalMs = CycleMilliseconds * _cycleCount;
        Ring.SetSchedule(_cycleMinutes, _schedule);
        Timeline.SetData(_cycleMinutes, _breaks);
    }

    private void UpdateAll()
    {
        RebuildSchedule();
        UpdateTimerVisuals();
        UpdateSettingsControls();
    }

    private void UpdateTimerVisuals()
    {
        var segment = CurrentSegment;
        var local = LocalMinutes;
        Ring.SetNow(local, _completed);

        var isRest = segment?.Kind == SegmentKind.Rest;
        var phaseBrush = _completed ? _goldBrush : isRest ? _greenBrush : _redBrush;

        CycleNumberText.Text = CurrentCycleIndex.ToString("00", CultureInfo.InvariantCulture);
        CycleTotalText.Text = $"/ {_cycleCount:00}";

        if (_completed)
        {
            PhaseText.Text = "完成";
            PhaseEnText.Text = "DONE";
            PhaseText.Foreground = _inkSoftBrush;
            PhaseEnText.Foreground = _inkSoftBrush;
            PhaseDot.Fill = _goldBrush;
            PhaseBadge.Background = (Brush)FindResource("SoftCardBrush");
            PhaseBadge.BorderBrush = _cardBorderBrush;
        }
        else if (isRest)
        {
            PhaseText.Text = "休息";
            PhaseEnText.Text = "BREAK";
            PhaseText.Foreground = _greenDeepBrush;
            PhaseEnText.Foreground = _greenDeepBrush;
            PhaseDot.Fill = _greenBrush;
            PhaseBadge.Background = _greenFaintBrush;
            PhaseBadge.BorderBrush = _greenSoftBrush;
        }
        else
        {
            PhaseText.Text = "专注";
            PhaseEnText.Text = "FOCUS";
            PhaseText.Foreground = _redDeepBrush;
            PhaseEnText.Foreground = _redDeepBrush;
            PhaseDot.Fill = _redBrush;
            PhaseBadge.Background = _redFaintBrush;
            PhaseBadge.BorderBrush = _redSoftBrush;
        }

        StartPauseButton.BorderBrush = phaseBrush;

        PlayGlyph.Visibility = _running ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = _running ? Visibility.Visible : Visibility.Collapsed;

        StartPauseButton.ToolTip = _running
            ? "暂停计时 (Space)"
            : _completed
                ? "重新开始 (Space)"
                : _elapsedMs > 0
                    ? "继续计时 (Space)"
                    : "开始专注 (Space)";

        SettingsStartButton.Content = _running
            ? "暂停计时"
            : _completed
                ? "重新开始"
                : _elapsedMs > 0
                    ? "继续计时"
                    : "开始计时";

        SettingsStartButton.IsEnabled = true;
        SettingsResetButton.IsEnabled = _elapsedMs > 0 || _running || _completed;
        SettingsSkipButton.IsEnabled = !_completed;

        UpdateBottomBar();

        if (_settingsOpen)
        {
            UpdateSettingsStatus();
        }
    }

    private void UpdateBottomBar()
    {
        if (!_loaded && _totalMs <= 0)
        {
            return;
        }

        var remainingMs = Math.Max(0, _totalMs - _elapsedMs);
        var ratio = _totalMs > 0 ? Math.Clamp(remainingMs / _totalMs, 0, 1) : 0;
        var redPercent = Math.Clamp((int)Math.Round(ratio * 100), 0, 100);
        var greenPercent = 100 - redPercent;
        var color = MixColor(ratio);

        RemainText.Text = FormatClock(remainingMs, true);
        ElapsedText.Text = FormatClock(_elapsedMs, false);
        TotalTimeText.Text = FormatClock(_totalMs, false);
        DutyText.Text = $"{redPercent}%";
        RedValueText.Text = redPercent.ToString(CultureInfo.InvariantCulture);
        GreenValueText.Text = greenPercent.ToString(CultureInfo.InvariantCulture);

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        BarFill.Background = brush;

        var available = BarTrackInner.ActualWidth;
        if (available > 0)
        {
            var width = available * ratio;
            BarFill.Width = width;
            BarFill.CornerRadius = new CornerRadius(Math.Min(4, width / 2));
        }

        var phaseText = _completed
            ? "已完成"
            : _running
                ? CurrentSegment?.Kind == SegmentKind.Rest ? "休息中" : "专注中"
                : _elapsedMs > 0
                    ? "已暂停"
                    : "等待开始";

        CycleStatusText.Text = phaseText;
    }

    private static Color MixColor(double redWeight)
    {
        var weight = Math.Clamp(redWeight, 0, 1);
        const int redR = 0xD8, redG = 0x4F, redB = 0x3C;
        const int greenR = 0x2F, greenG = 0x8A, greenB = 0x60;

        return Color.FromRgb(
            (byte)Math.Round(redR * weight + greenR * (1 - weight)),
            (byte)Math.Round(redG * weight + greenG * (1 - weight)),
            (byte)Math.Round(redB * weight + greenB * (1 - weight)));
    }

    private static string FormatClock(double milliseconds, bool roundUp)
    {
        var totalSeconds = Math.Max(0, (int)(roundUp ? Math.Ceiling(milliseconds / 1000d) : Math.Floor(milliseconds / 1000d)));
        var hours = totalSeconds / 3600;
        totalSeconds %= 3600;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    private static string FormatTotalDuration(int totalMinutes)
    {
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours > 0 && minutes > 0)
        {
            return $"{hours} 小时 {minutes} 分钟";
        }

        if (hours > 0)
        {
            return $"{hours} 小时";
        }

        return $"{minutes} 分钟";
    }

    private void UpdateSettingsLayout()
    {
        if (SettingsView.ActualWidth <= 0)
        {
            return;
        }

        var available = Math.Max(320, SettingsView.ActualWidth - 60);
        SettingsContent.Width = Math.Min(1040, available);

        var narrow = SettingsContent.Width < 880;
        if (narrow)
        {
            SettingsColumns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            SettingsColumns.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(SettingsLeftCard, 0);
            Grid.SetColumnSpan(SettingsLeftCard, 2);
            Grid.SetRow(SettingsLeftCard, 0);
            SettingsLeftCard.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetColumn(SettingsRightPanel, 0);
            Grid.SetColumnSpan(SettingsRightPanel, 2);
            Grid.SetRow(SettingsRightPanel, 1);
            SettingsRightPanel.Margin = new Thickness(0);
        }
        else
        {
            SettingsColumns.ColumnDefinitions[0].Width = new GridLength(1.48, GridUnitType.Star);
            SettingsColumns.ColumnDefinitions[1].Width = new GridLength(0.9, GridUnitType.Star);
            Grid.SetColumn(SettingsLeftCard, 0);
            Grid.SetColumnSpan(SettingsLeftCard, 1);
            Grid.SetRow(SettingsLeftCard, 0);
            SettingsLeftCard.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(SettingsRightPanel, 1);
            Grid.SetColumnSpan(SettingsRightPanel, 1);
            Grid.SetRow(SettingsRightPanel, 0);
            SettingsRightPanel.Margin = new Thickness(10, 0, 0, 0);
        }
    }

    private void UpdateSettingsControls()
    {
        _updatingUi = true;

        if (!CycleLengthBox.IsKeyboardFocusWithin)
        {
            CycleLengthBox.Text = _cycleMinutes.ToString(CultureInfo.InvariantCulture);
        }

        if (!CycleCountBox.IsKeyboardFocusWithin)
        {
            CycleCountBox.Text = _cycleCount.ToString(CultureInfo.InvariantCulture);
        }

        BreakCountText.Text = _breaks.Count.ToString("00", CultureInfo.InvariantCulture);
        TotalDurationText.Text = FormatTotalDuration(_cycleMinutes * _cycleCount);
        CycleBreakdownText.Text = $"{_cycleMinutes} 分 × {_cycleCount}";
        Timeline.SetData(_cycleMinutes, _breaks);
        RebuildBreakList();
        UpdatePresetChecks();
        AddBreakButton.IsEnabled = _schedule.Any(segment => segment.Kind == SegmentKind.Work && segment.Duration >= 7);

        _updatingUi = false;
        UpdateSettingsStatus();
    }

    private void UpdateSettingsStatus()
    {
        if (_completed)
        {
            SettingsStatusText.Text = "已完成 · 全部循环结束";
            return;
        }

        if (_running)
        {
            var phase = CurrentSegment?.Kind == SegmentKind.Rest ? "休息" : "专注";
            SettingsStatusText.Text = $"运行中 · {phase} · 还剩 {FormatClock(Math.Max(0, _totalMs - _elapsedMs), true)}";
            return;
        }

        if (_elapsedMs > 0)
        {
            SettingsStatusText.Text = $"已暂停 · 还剩 {FormatClock(Math.Max(0, _totalMs - _elapsedMs), true)}";
            return;
        }

        SettingsStatusText.Text = "等待开始";
    }

    private void UpdatePresetChecks()
    {
        var signature = ScheduleBuilder.Signature(_cycleMinutes, _cycleCount, _breaks);
        PresetClassic.IsChecked = ScheduleBuilder.Signature(
            _presets["classic"].CycleMinutes,
            _presets["classic"].Cycles,
            _presets["classic"].Breaks) == signature;
        PresetDouble.IsChecked = ScheduleBuilder.Signature(
            _presets["double"].CycleMinutes,
            _presets["double"].Cycles,
            _presets["double"].Breaks) == signature;
        PresetDeep.IsChecked = ScheduleBuilder.Signature(
            _presets["deep"].CycleMinutes,
            _presets["deep"].Cycles,
            _presets["deep"].Breaks) == signature;
    }

    private void RebuildBreakList()
    {
        BreakListPanel.Children.Clear();
        _breakEditorBoxes.Clear();

        var ordered = _breaks.OrderBy(item => item.Start).ThenBy(item => item.End).ToList();
        if (ordered.Count == 0)
        {
            BreakListPanel.Children.Add(new TextBlock
            {
                Text = "当前循环没有休息区间，整个循环都是工作时间。",
                Style = (Style)FindResource("TinyTextStyle"),
                Foreground = (Brush)FindResource("MutedLightBrush"),
                Margin = new Thickness(1, 11, 0, 5),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            BreakListPanel.Children.Add(CreateBreakRow(ordered[index], index));
        }
    }

    private Border CreateBreakRow(BreakInterval breakItem, int index)
    {
        var row = new Border
        {
            BorderBrush = _lineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 6, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.Child = grid;

        var badge = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = (Brush)FindResource("GreenFaintBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = (index + 1).ToString("00", CultureInfo.InvariantCulture),
                FontFamily = new FontFamily("Bahnschrift"),
                FontSize = 7.5,
                Foreground = (Brush)FindResource("GreenDeepBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var startField = CreateBreakField("开始", breakItem, true);
        Grid.SetColumn(startField, 1);
        grid.Children.Add(startField);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = 9,
            Foreground = (Brush)FindResource("MutedLightBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        var endField = CreateBreakField("结束", breakItem, false);
        Grid.SetColumn(endField, 3);
        grid.Children.Add(endField);

        var durationText = new TextBlock
        {
            Text = $"{breakItem.Duration} min",
            FontFamily = new FontFamily("Bahnschrift"),
            FontSize = 8,
            Foreground = (Brush)FindResource("GreenDeepBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetColumn(durationText, 4);
        grid.Children.Add(durationText);

        var remove = new Button
        {
            Content = "×",
            Style = (Style)FindResource("TextButtonStyle"),
            FontSize = 14,
            Width = 20,
            Height = 22,
            Tag = breakItem,
            ToolTip = "删除此休息区间",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        remove.Click += RemoveBreak_Click;
        Grid.SetColumn(remove, 5);
        grid.Children.Add(remove);

        return row;
    }

    private FrameworkElement CreateBreakField(string labelText, BreakInterval breakItem, bool isStart)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = labelText,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 7.5,
            Foreground = (Brush)FindResource("MutedBrush")
        });

        var inputGrid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        var box = new TextBox
        {
            Style = (Style)FindResource("MiniTextBoxStyle"),
            Text = (isStart ? breakItem.Start : breakItem.End).ToString(CultureInfo.InvariantCulture),
            Padding = new Thickness(7, 0, 16, 0),
            Tag = new BreakEditTag(breakItem, isStart)
        };
        box.LostFocus += BreakTextBox_LostFocus;
        box.PreviewKeyDown += BreakTextBox_PreviewKeyDown;
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        inputGrid.Children.Add(box);

        inputGrid.Children.Add(new TextBlock
        {
            Text = "′",
            FontFamily = new FontFamily("Bahnschrift"),
            FontSize = 8,
            Foreground = (Brush)FindResource("MutedLightBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            IsHitTestVisible = false
        });

        stack.Children.Add(inputGrid);
        _breakEditorBoxes.Add((new BreakEditTag(breakItem, isStart), box));
        return stack;
    }

    private void Timeline_PreviewChanged(object? sender, EventArgs e)
    {
        _updatingUi = true;
        foreach (var entry in _breakEditorBoxes)
        {
            entry.Box.Text = (entry.Tag.IsStart ? entry.Tag.Break.Start : entry.Tag.Break.End)
                .ToString(CultureInfo.InvariantCulture);
        }
        _updatingUi = false;
    }

    private void Timeline_Edited(object? sender, EventArgs e)
    {
        var hadProgress = ResetForConfigChange();
        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress ? "休息区间已更新 · 计时已重置" : "休息区间已更新";
    }

    private bool ResetForConfigChange()
    {
        var hadProgress = _elapsedMs > 0 || _running || _completed;
        _running = false;
        _completed = false;
        _elapsedMs = 0;
        _lastRenderedSecond = -1;
        return hadProgress;
    }

    private void BreakTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            CommitBreakTextBox(box);
        }
    }

    private void BreakTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            CommitBreakTextBox(box);
            e.Handled = true;
        }
    }

    private void CommitBreakTextBox(TextBox box)
    {
        if (_updatingUi || box.Tag is not BreakEditTag tag)
        {
            return;
        }

        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            box.Text = (tag.IsStart ? tag.Break.Start : tag.Break.End).ToString(CultureInfo.InvariantCulture);
            return;
        }

        var ordered = _breaks.OrderBy(item => item.Start).ThenBy(item => item.End).ToList();
        var position = ordered.IndexOf(tag.Break);
        if (position < 0)
        {
            return;
        }

        var previous = position > 0 ? ordered[position - 1] : null;
        var next = position < ordered.Count - 1 ? ordered[position + 1] : null;

        int min;
        int max;
        if (tag.IsStart)
        {
            min = previous?.End ?? 0;
            max = tag.Break.End - 1;
        }
        else
        {
            min = tag.Break.Start + 1;
            max = next?.Start ?? _cycleMinutes;
        }

        value = Math.Clamp(value, min, max);
        if (tag.IsStart)
        {
            if (tag.Break.Start == value)
            {
                box.Text = value.ToString(CultureInfo.InvariantCulture);
                return;
            }
        }
        else if (tag.Break.End == value)
        {
            box.Text = value.ToString(CultureInfo.InvariantCulture);
            return;
        }

        var hadProgress = ResetForConfigChange();
        if (tag.IsStart)
        {
            tag.Break.Start = value;
        }
        else
        {
            tag.Break.End = value;
        }

        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress ? "休息区间已更新 · 计时已重置" : "休息区间已更新";
    }

    private void AddBreakButton_Click(object sender, RoutedEventArgs e)
    {
        var target = _schedule
            .Where(segment => segment.Kind == SegmentKind.Work && segment.Duration >= 7)
            .OrderByDescending(segment => segment.Duration)
            .FirstOrDefault();

        if (target is null)
        {
            SettingsStatusText.Text = "当前循环没有足够空间再加入休息区间";
            return;
        }

        var duration = 5;
        var available = target.Duration;
        var start = (int)Math.Round(target.Start + (available - duration) / 2d);
        start = Math.Clamp(start, (int)target.Start + 1, (int)target.End - duration - 1);

        var hadProgress = ResetForConfigChange();
        _breaks.Add(new BreakInterval(start, start + duration));
        _breaks.Sort((a, b) => a.Start.CompareTo(b.Start));
        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress ? "已增加休息区间 · 计时已重置" : "已增加休息区间";
    }

    private void RemoveBreak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BreakInterval breakItem })
        {
            return;
        }

        var hadProgress = ResetForConfigChange();
        _breaks.Remove(breakItem);
        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress ? "已删除休息区间 · 计时已重置" : "已删除休息区间";
    }

    private void CycleLengthMinus_Click(object sender, RoutedEventArgs e) => SetCycleLength(_cycleMinutes - 1);

    private void CycleLengthPlus_Click(object sender, RoutedEventArgs e) => SetCycleLength(_cycleMinutes + 1);

    private void CycleCountMinus_Click(object sender, RoutedEventArgs e) => SetCycleCount(_cycleCount - 1);

    private void CycleCountPlus_Click(object sender, RoutedEventArgs e) => SetCycleCount(_cycleCount + 1);

    private void CycleLengthBox_LostFocus(object sender, RoutedEventArgs e) => CommitCycleLengthBox();

    private void CycleLengthBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCycleLengthBox();
            e.Handled = true;
        }
    }

    private void CommitCycleLengthBox()
    {
        if (_updatingUi)
        {
            return;
        }

        if (!int.TryParse(CycleLengthBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            CycleLengthBox.Text = _cycleMinutes.ToString(CultureInfo.InvariantCulture);
            return;
        }

        SetCycleLength(value);
    }

    private void SetCycleLength(int value)
    {
        value = Math.Clamp(value, ScheduleBuilder.MinCycleMinutes, ScheduleBuilder.MaxCycleMinutes);
        if (value == _cycleMinutes)
        {
            CycleLengthBox.Text = value.ToString(CultureInfo.InvariantCulture);
            return;
        }

        var hadProgress = ResetForConfigChange();
        _cycleMinutes = value;

        for (var i = _breaks.Count - 1; i >= 0; i--)
        {
            var breakItem = _breaks[i];
            if (breakItem.Start >= value || breakItem.End <= 0)
            {
                _breaks.RemoveAt(i);
                continue;
            }

            breakItem.Start = Math.Clamp(breakItem.Start, 0, value - 1);
            breakItem.End = Math.Clamp(breakItem.End, breakItem.Start + 1, value);
        }

        _breaks.Sort((a, b) => a.Start.CompareTo(b.Start));
        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress ? "循环总长已更新 · 计时已重置" : "循环总长已更新";
    }

    private void CycleCountBox_LostFocus(object sender, RoutedEventArgs e) => CommitCycleCountBox();

    private void CycleCountBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCycleCountBox();
            e.Handled = true;
        }
    }

    private void CommitCycleCountBox()
    {
        if (_updatingUi)
        {
            return;
        }

        if (!int.TryParse(CycleCountBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            CycleCountBox.Text = _cycleCount.ToString(CultureInfo.InvariantCulture);
            return;
        }

        SetCycleCount(value);
    }

    private void SetCycleCount(int value)
    {
        value = Math.Clamp(value, ScheduleBuilder.MinCycles, ScheduleBuilder.MaxCycles);
        if (value == _cycleCount)
        {
            CycleCountBox.Text = value.ToString(CultureInfo.InvariantCulture);
            return;
        }

        var hadProgress = ResetForConfigChange();
        _cycleCount = value;
        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress ? "循环次数已更新 · 计时已重置" : "循环次数已更新";
    }

    private void ApplyPreset(string key)
    {
        if (!_presets.TryGetValue(key, out var preset))
        {
            return;
        }

        var currentSignature = ScheduleBuilder.Signature(_cycleMinutes, _cycleCount, _breaks);
        var presetSignature = ScheduleBuilder.Signature(preset.CycleMinutes, preset.Cycles, preset.Breaks);
        if (currentSignature == presetSignature)
        {
            UpdatePresetChecks();
            return;
        }

        var hadProgress = ResetForConfigChange();
        _cycleMinutes = preset.CycleMinutes;
        _cycleCount = preset.Cycles;
        _breaks.Clear();
        _breaks.AddRange(preset.Breaks.Select(item => item.Clone()));
        RebuildSchedule();
        UpdateAll();
        SettingsStatusText.Text = hadProgress
            ? $"已载入「{preset.Name}」· 计时已重置"
            : $"已载入「{preset.Name}」";
    }

    private void PresetClassic_Click(object sender, RoutedEventArgs e) => ApplyPreset("classic");

    private void PresetDouble_Click(object sender, RoutedEventArgs e) => ApplyPreset("double");

    private void PresetDeep_Click(object sender, RoutedEventArgs e) => ApplyPreset("deep");

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        ApplyPreset("double");
        SettingsStatusText.Text = "已恢复默认循环";
    }

    private void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            PauseTimer();
        }
        else
        {
            StartTimer();
        }
    }

    private void StartTimer()
    {
        if (_running)
        {
            return;
        }

        if (_completed)
        {
            _elapsedMs = 0;
            _completed = false;
            _lastRenderedSecond = -1;
        }

        _running = true;
        _lastTick = Stopwatch.GetTimestamp();
        UpdateTimerVisuals();
        UpdateSettingsStatus();
    }

    private void PauseTimer()
    {
        if (!_running)
        {
            return;
        }

        AdvanceElapsed();
        _running = false;
        UpdateTimerVisuals();
        UpdateSettingsStatus();
    }

    private void ResetTimerButton_Click(object sender, RoutedEventArgs e)
    {
        _running = false;
        _completed = false;
        _elapsedMs = 0;
        _lastRenderedSecond = -1;
        UpdateTimerVisuals();
        UpdateSettingsStatus();
    }

    private void SkipStageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            return;
        }

        var segment = CurrentSegment;
        if (segment is null)
        {
            return;
        }

        var cycleIndex = Math.Floor(_elapsedMs / CycleMilliseconds);
        var target = Math.Min(_totalMs, cycleIndex * CycleMilliseconds + segment.End * 60_000d);
        _elapsedMs = target;
        _lastRenderedSecond = -1;

        if (_elapsedMs >= _totalMs - 0.5)
        {
            _elapsedMs = _totalMs;
            _completed = true;
            _running = false;
        }

        UpdateTimerVisuals();
        UpdateSettingsStatus();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettings();
    }

    private void ToggleSettings()
    {
        _settingsOpen = !_settingsOpen;
        TimerView.Visibility = _settingsOpen ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        SettingsButtonText.Text = _settingsOpen ? "返回" : "设置";
        SettingsButton.ToolTip = _settingsOpen ? "返回计时" : "循环设置";
        SettingsSlidersGlyph.Visibility = _settingsOpen ? Visibility.Collapsed : Visibility.Visible;
        BackGlyph.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;

        if (_settingsOpen)
        {
            UpdateSettingsLayout();
            UpdateSettingsControls();
            SettingsScrollViewer.ScrollToHome();
            Dispatcher.BeginInvoke(UpdateSettingsLayout, DispatcherPriority.Loaded);
        }
        else
        {
            UpdateTimerVisuals();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeVisuals()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        AutomationProperties.SetName(MaximizeButton, maximized ? "还原窗口" : "最大化");
    }

    private void BarTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBottomBar();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifier = Keyboard.Modifiers;
        if (e.Key == Key.Escape && _settingsOpen)
        {
            ToggleSettings();
            e.Handled = true;
            return;
        }

        if ((modifier & ModifierKeys.Control) != 0 && e.Key == Key.OemComma)
        {
            ToggleSettings();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is ButtonBase)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            if (_settingsOpen)
            {
                return;
            }

            if (_running)
            {
                PauseTimer();
            }
            else
            {
                StartTimer();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.R && !_settingsOpen)
        {
            _running = false;
            _completed = false;
            _elapsedMs = 0;
            _lastRenderedSecond = -1;
            UpdateTimerVisuals();
            e.Handled = true;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        if (message == wmGetMinMaxInfo)
        {
            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, 2);
            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                minMaxInfo.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
                minMaxInfo.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
                minMaxInfo.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
                minMaxInfo.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
                Marshal.StructureToPtr(minMaxInfo, lParam, true);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public NATIVE_POINT ptReserved;
        public NATIVE_POINT ptMaxSize;
        public NATIVE_POINT ptMaxPosition;
        public NATIVE_POINT ptMinTrackSize;
        public NATIVE_POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public NATIVE_RECT rcMonitor;
        public NATIVE_RECT rcWork;
        public uint dwFlags;
    }
}
