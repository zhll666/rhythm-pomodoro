using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RhythmPomodoro.Controls;

public sealed class TimelineEditor : FrameworkElement
{
    private enum DragMode
    {
        None,
        Move,
        Start,
        End
    }

    private static readonly Brush WorkAreaBrush = CreateWorkAreaBrush();
    private static readonly Brush TrackBorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xA8, 0x9F));
    private static readonly Brush RestBrush = CreateRestBrush();
    private static readonly Brush RestBorderBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x5D, 0x40));
    private static readonly Brush HandleBrush = new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xFC, 0xF4));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(48, 0x8F, 0x2F, 0x23));
    private static readonly Brush RulerBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0x77, 0x6A));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFC, 0xF6));
    private static readonly Brush InkBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x1A, 0x17));

    private readonly Typeface _uiTypeface = new(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _uiBoldTypeface = new(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly Typeface _numberTypeface = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private IReadOnlyList<BreakInterval> _breaks = Array.Empty<BreakInterval>();
    private int _cycleMinutes = 50;

    private BreakInterval? _dragBreak;
    private DragMode _dragMode;
    private Point _dragStartPoint;
    private int _originalStart;
    private int _originalEnd;
    private bool _hasDragged;
    private const double TrackTop = 22;
    private const double TrackHeight = 42;
    private const double RulerHeight = 18;
    private const double SidePadding = 10;

    static TimelineEditor()
    {
        WorkAreaBrush.Freeze();
        TrackBorderBrush.Freeze();
        RestBrush.Freeze();
        RestBorderBrush.Freeze();
        HandleBrush.Freeze();
        GridBrush.Freeze();
        RulerBrush.Freeze();
        LabelBrush.Freeze();
        InkBrush.Freeze();
    }

    public event EventHandler? PreviewChanged;
    public event EventHandler? Edited;

    public TimelineEditor()
    {
        Focusable = true;
        ClipToBounds = false;
        Cursor = Cursors.Arrow;
        ToolTip = "拖动绿色区块移动休息时间，拖动两端调整开始或结束。";
    }

    public void SetData(int cycleMinutes, IReadOnlyList<BreakInterval> breaks)
    {
        _cycleMinutes = Math.Max(1, cycleMinutes);
        _breaks = breaks;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        if (width < 80 || _cycleMinutes <= 0)
        {
            return;
        }

        var trackLeft = SidePadding;
        var trackWidth = Math.Max(10, width - SidePadding * 2);
        var trackRect = new Rect(trackLeft, TrackTop, trackWidth, TrackHeight);
        var radius = 7.0;

        dc.DrawRoundedRectangle(WorkAreaBrush, new Pen(TrackBorderBrush, 1), trackRect, radius, radius);

        for (var minute = 5; minute < _cycleMinutes; minute += 5)
        {
            var x = trackLeft + trackWidth * minute / _cycleMinutes;
            dc.DrawLine(new Pen(GridBrush, 1), new Point(x, TrackTop + 1), new Point(x, TrackTop + TrackHeight - 1));
        }

        foreach (var breakItem in _breaks.OrderBy(item => item.Start))
        {
            var left = trackLeft + trackWidth * breakItem.Start / _cycleMinutes;
            var right = trackLeft + trackWidth * breakItem.End / _cycleMinutes;
            var blockRect = new Rect(left + 1, TrackTop + 5, Math.Max(2, right - left - 2), TrackHeight - 10);
            var blockRadius = Math.Min(5, blockRect.Width / 2);

            dc.DrawRoundedRectangle(RestBrush, new Pen(RestBorderBrush, 1.1), blockRect, blockRadius, blockRadius);

            var handleTop = blockRect.Top + 4;
            var handleHeight = blockRect.Height - 8;
            if (blockRect.Width > 8)
            {
                dc.DrawRoundedRectangle(HandleBrush, null,
                    new Rect(blockRect.Left + 3, handleTop, 2.4, handleHeight), 1.2, 1.2);
                dc.DrawRoundedRectangle(HandleBrush, null,
                    new Rect(blockRect.Right - 5.4, handleTop, 2.4, handleHeight), 1.2, 1.2);
            }

            if (blockRect.Width > 34)
            {
                var label = $"休息 {breakItem.Duration}′";
                var text = MakeText(label, 8.8, LabelBrush, _uiBoldTypeface);
                if (text.Width < blockRect.Width - 12)
                {
                    dc.DrawText(text, new Point(
                        blockRect.Left + (blockRect.Width - text.Width) / 2,
                        blockRect.Top + (blockRect.Height - text.Height) / 2));
                }
            }
        }

        var labels = BuildRulerLabels(_cycleMinutes);
        foreach (var minute in labels)
        {
            var x = trackLeft + trackWidth * minute / _cycleMinutes;
            var linePen = new Pen(RulerBrush, 0.8);
            linePen.Freeze();
            dc.DrawLine(linePen, new Point(x, TrackTop + TrackHeight), new Point(x, TrackTop + TrackHeight + 3));

            var text = MakeText(minute.ToString(CultureInfo.InvariantCulture), 8.2, RulerBrush, _numberTypeface);
            var drawX = Math.Clamp(x - text.Width / 2, SidePadding, width - SidePadding - text.Width);
            dc.DrawText(text, new Point(drawX, TrackTop + TrackHeight + 3.2));
        }

        var trackBorderPen = new Pen(TrackBorderBrush, 1);
        trackBorderPen.Freeze();
        dc.DrawRoundedRectangle(null, trackBorderPen, trackRect, radius, radius);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var position = e.GetPosition(this);
        var hit = HitTestBreak(position);
        if (hit is null)
        {
            return;
        }

        Focus();
        CaptureMouse();
        _dragBreak = hit.Value.Break;
        _dragMode = hit.Value.Mode;
        _dragStartPoint = position;
        _originalStart = _dragBreak.Start;
        _originalEnd = _dragBreak.End;
        _hasDragged = false;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);

        if (_dragBreak is not null && IsMouseCaptured)
        {
            var trackWidth = Math.Max(10, ActualWidth - SidePadding * 2);
            var delta = (int)Math.Round((position.X - _dragStartPoint.X) / trackWidth * _cycleMinutes);
            if (delta == 0 && !_hasDragged)
            {
                return;
            }

            var ordered = _breaks.OrderBy(item => item.Start).ToList();
            var index = ordered.IndexOf(_dragBreak);
            if (index < 0)
            {
                return;
            }

            var previous = index > 0 ? ordered[index - 1] : null;
            var next = index < ordered.Count - 1 ? ordered[index + 1] : null;
            var lower = previous?.End ?? 0;
            var upper = next?.Start ?? _cycleMinutes;

            var newStart = _dragBreak.Start;
            var newEnd = _dragBreak.End;

            switch (_dragMode)
            {
                case DragMode.Start:
                    newStart = Math.Clamp(_originalStart + delta, lower, _dragBreak.End - 1);
                    break;
                case DragMode.End:
                    newEnd = Math.Clamp(_originalEnd + delta, _dragBreak.Start + 1, upper);
                    break;
                case DragMode.Move:
                {
                    var duration = _originalEnd - _originalStart;
                    newStart = Math.Clamp(_originalStart + delta, lower, Math.Max(lower, upper - duration));
                    newEnd = newStart + duration;
                    break;
                }
            }

            if (newStart == _dragBreak.Start && newEnd == _dragBreak.End)
            {
                return;
            }

            _dragBreak.Start = newStart;
            _dragBreak.End = newEnd;
            _hasDragged = true;
            InvalidateVisual();
            PreviewChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var hover = HitTestBreak(position);
        Cursor = hover?.Mode switch
        {
            DragMode.Start or DragMode.End => Cursors.SizeWE,
            DragMode.Move => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!IsMouseCaptured)
        {
            return;
        }

        ReleaseMouseCapture();
        var wasDragging = _hasDragged;
        _dragBreak = null;
        _dragMode = DragMode.None;
        _hasDragged = false;

        if (wasDragging)
        {
            Edited?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured)
        {
            Cursor = Cursors.Arrow;
        }
    }

    private (BreakInterval Break, DragMode Mode)? HitTestBreak(Point point)
    {
        if (point.Y < TrackTop + 2 || point.Y > TrackTop + TrackHeight - 2)
        {
            return null;
        }

        var trackWidth = Math.Max(10, ActualWidth - SidePadding * 2);
        foreach (var breakItem in _breaks.OrderBy(item => item.Start))
        {
            var left = SidePadding + trackWidth * breakItem.Start / _cycleMinutes;
            var right = SidePadding + trackWidth * breakItem.End / _cycleMinutes;
            if (point.X < left - 2 || point.X > right + 2)
            {
                continue;
            }

            if (Math.Abs(point.X - left) <= 8)
            {
                return (breakItem, DragMode.Start);
            }

            if (Math.Abs(point.X - right) <= 8)
            {
                return (breakItem, DragMode.End);
            }

            return (breakItem, DragMode.Move);
        }

        return null;
    }

    private FormattedText MakeText(string text, double size, Brush brush, Typeface typeface)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static IEnumerable<int> BuildRulerLabels(int cycleMinutes)
    {
        var step = cycleMinutes <= 30
            ? 5
            : cycleMinutes <= 90
                ? 10
                : cycleMinutes <= 150
                    ? 20
                    : 30;

        var labels = new List<int>();
        for (var minute = 0; minute < cycleMinutes; minute += step)
        {
            labels.Add(minute);
        }

        if (labels[^1] != cycleMinutes)
        {
            if (cycleMinutes - labels[^1] < step * 0.45)
            {
                labels[^1] = cycleMinutes;
            }
            else
            {
                labels.Add(cycleMinutes);
            }
        }

        return labels;
    }

    private static Brush CreateWorkAreaBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0xF5, 0xDF, 0xDA)),
            null,
            new RectangleGeometry(new Rect(0, 0, 10, 10))));

        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(38, 0xCB, 0x52, 0x41)), 1.2);
        linePen.Freeze();
        group.Children.Add(new GeometryDrawing(
            null,
            linePen,
            new LineGeometry(new Point(-2, 12), new Point(12, -2))));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 10, 10),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        brush.Freeze();
        return brush;
    }

    private static Brush CreateRestBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0x40, 0x99, 0x71),
            Color.FromRgb(0x2A, 0x7E, 0x59),
            new Point(0, 0),
            new Point(0, 1));
        brush.Freeze();
        return brush;
    }
}
