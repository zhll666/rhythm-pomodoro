using System.Windows;
using System.Windows.Media;

namespace RhythmPomodoro.Controls;

public sealed class PomodoroRing : FrameworkElement
{
    private static readonly Brush WorkFill = CreateLinearGradient(
        Color.FromRgb(0xE2, 0x60, 0x4D),
        Color.FromRgb(0xCB, 0x3C, 0x2E));

    private static readonly Brush RestFill = CreateLinearGradient(
        Color.FromRgb(0x3E, 0x9B, 0x70),
        Color.FromRgb(0x23, 0x76, 0x51));

    private static readonly Brush PastWorkFill = CreateHatchBrush(
        Color.FromRgb(0xF2, 0xD7, 0xD0),
        Color.FromRgb(0xDE, 0xA4, 0x97));

    private static readonly Brush PastRestFill = CreateHatchBrush(
        Color.FromRgb(0xE0, 0xED, 0xE4),
        Color.FromRgb(0xAA, 0xCC, 0xB9));

    private static readonly Pen WorkStroke = CreatePen(Color.FromRgb(0x8F, 0x2F, 0x23), 1.15);
    private static readonly Pen RestStroke = CreatePen(Color.FromRgb(0x1B, 0x5D, 0x40), 1.15);

    private static readonly Pen PastWorkStroke = new(
        new SolidColorBrush(Color.FromArgb(90, 0x8F, 0x2F, 0x23)), 1.0)
    {
        DashStyle = new DashStyle(new double[] { 2, 2.6 }, 0)
    };

    private static readonly Pen PastRestStroke = new(
        new SolidColorBrush(Color.FromArgb(90, 0x1B, 0x5D, 0x40)), 1.0)
    {
        DashStyle = new DashStyle(new double[] { 2, 2.6 }, 0)
    };

    private static readonly Brush TickMinorBrush =
        new SolidColorBrush(Color.FromArgb(150, 0x9A, 0x91, 0x83));

    private static readonly Brush TickMajorBrush =
        new SolidColorBrush(Color.FromArgb(205, 0x52, 0x4C, 0x42));

    private static readonly Brush InkBrush =
        new SolidColorBrush(Color.FromRgb(0x1B, 0x1A, 0x17));

    private static readonly Brush PointerUnderlayBrush =
        new SolidColorBrush(Color.FromArgb(228, 0xF8, 0xF3, 0xE8));

    private static readonly Brush GoldBrush =
        new SolidColorBrush(Color.FromRgb(0xC6, 0x9A, 0x56));

    private IReadOnlyList<CycleSegment> _schedule = Array.Empty<CycleSegment>();
    private int _cycleMinutes = 50;
    private double _nowMinutes;
    private bool _completed;

    static PomodoroRing()
    {
        WorkStroke.Freeze();
        RestStroke.Freeze();
        PastWorkStroke.Freeze();
        PastRestStroke.Freeze();
        TickMinorBrush.Freeze();
        TickMajorBrush.Freeze();
        InkBrush.Freeze();
        PointerUnderlayBrush.Freeze();
        GoldBrush.Freeze();
    }

    public void SetSchedule(int cycleMinutes, IReadOnlyList<CycleSegment> schedule)
    {
        _cycleMinutes = Math.Max(1, cycleMinutes);
        _schedule = schedule;
        InvalidateVisual();
    }

    public void SetNow(double nowMinutes, bool completed)
    {
        _nowMinutes = Math.Clamp(nowMinutes, 0, _cycleMinutes);
        _completed = completed;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 80)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var outerRadius = size * 0.435;
        var ringThickness = size * 0.056;
        var innerRadius = outerRadius - ringThickness;

        DrawDialBase(dc, center, innerRadius);
        DrawTicks(dc, center, innerRadius);
        DrawSegments(dc, center, outerRadius, innerRadius);
        DrawOrbit(dc, center, outerRadius);

        if (!_completed)
        {
            DrawPointer(dc, center, outerRadius, innerRadius);
        }
    }

    private static void DrawDialBase(DrawingContext dc, Point center, double innerRadius)
    {
        var faceBrush = new RadialGradientBrush(
            Color.FromRgb(0xFC, 0xFA, 0xF5),
            Color.FromRgb(0xF4, 0xEF, 0xE5))
        {
            GradientOrigin = new Point(0.44, 0.35),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.68,
            RadiusY = 0.68
        };
        faceBrush.Freeze();

        dc.DrawEllipse(faceBrush, null, center, innerRadius - 4, innerRadius - 4);

        var innerLine = new Pen(new SolidColorBrush(Color.FromArgb(32, 0x66, 0x5C, 0x4C)), 0.8);
        innerLine.Freeze();
        dc.DrawEllipse(null, innerLine, center, innerRadius - 25, innerRadius - 25);
    }

    private void DrawTicks(DrawingContext dc, Point center, double innerRadius)
    {
        var tickOuter = innerRadius - 5;
        var minorPen = new Pen(TickMinorBrush, 0.75);
        var majorPen = new Pen(TickMajorBrush, 1.35);
        minorPen.Freeze();
        majorPen.Freeze();

        for (var minute = 0; minute < _cycleMinutes; minute++)
        {
            var angle = -90d + (360d * minute / _cycleMinutes);
            var major = minute % 5 == 0;
            var tickInner = tickOuter - (major ? 8.2 : 4.2);
            var p1 = Polar(center, tickOuter, angle);
            var p2 = Polar(center, tickInner, angle);
            dc.DrawLine(major ? majorPen : minorPen, p1, p2);
        }
    }

    private void DrawSegments(DrawingContext dc, Point center, double outerRadius, double innerRadius)
    {
        var slices = new List<(CycleSegment Segment, double Start, double End, bool Past)>();

        foreach (var segment in _schedule)
        {
            if (_nowMinutes >= segment.End - 0.0001)
            {
                slices.Add((segment, segment.Start, segment.End, true));
            }
            else if (_nowMinutes <= segment.Start + 0.0001)
            {
                slices.Add((segment, segment.Start, segment.End, false));
            }
            else
            {
                slices.Add((segment, segment.Start, _nowMinutes, true));
                slices.Add((segment, _nowMinutes, segment.End, false));
            }
        }

        var gap = 0.16;
        foreach (var slice in slices)
        {
            if (slice.End - slice.Start < 0.002)
            {
                continue;
            }

            var startAngle = -90d + (360d * slice.Start / _cycleMinutes) + gap;
            var endAngle = -90d + (360d * slice.End / _cycleMinutes) - gap;
            if (endAngle <= startAngle)
            {
                continue;
            }

            var geometry = CreateAnnularSector(center, outerRadius, innerRadius, startAngle, endAngle);
            var fill = slice.Past
                ? slice.Segment.Kind == SegmentKind.Work ? PastWorkFill : PastRestFill
                : slice.Segment.Kind == SegmentKind.Work ? WorkFill : RestFill;

            var stroke = slice.Past
                ? slice.Segment.Kind == SegmentKind.Work ? PastWorkStroke : PastRestStroke
                : slice.Segment.Kind == SegmentKind.Work ? WorkStroke : RestStroke;

            dc.DrawGeometry(fill, stroke, geometry);
        }
    }

    private static void DrawOrbit(DrawingContext dc, Point center, double outerRadius)
    {
        var orbitPen = new Pen(new SolidColorBrush(Color.FromArgb(74, 0x6D, 0x63, 0x53)), 0.9)
        {
            DashStyle = new DashStyle(new double[] { 1, 7 }, 0)
        };
        orbitPen.Freeze();

        dc.DrawEllipse(null, orbitPen, center, outerRadius + 8, outerRadius + 8);

        var innerPen = new Pen(new SolidColorBrush(Color.FromArgb(22, 0x6D, 0x63, 0x53)), 0.8);
        innerPen.Freeze();
        dc.DrawEllipse(null, innerPen, center, outerRadius + 10.5, outerRadius + 10.5);
    }

    private void DrawPointer(DrawingContext dc, Point center, double outerRadius, double innerRadius)
    {
        // The pointer geometry is authored pointing to 12 o'clock, so 0° here
        // means the initial (top) position.
        var angle = 360d * _nowMinutes / _cycleMinutes;
        var underlayPen = new Pen(PointerUnderlayBrush, 5.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var inkPen = new Pen(InkBrush, 1.9)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        underlayPen.Freeze();
        inkPen.Freeze();

        dc.PushTransform(new RotateTransform(angle, center.X, center.Y));

        var baseRadius = innerRadius - 20;
        var tipRadius = outerRadius - 4;
        var lineStart = new Point(center.X, center.Y - baseRadius);
        var lineEnd = new Point(center.X, center.Y - tipRadius + 8);
        dc.DrawLine(underlayPen, lineStart, lineEnd);
        dc.DrawLine(inkPen, lineStart, lineEnd);

        var arrow = new StreamGeometry();
        using (var context = arrow.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y - tipRadius), true, true);
            context.LineTo(new Point(center.X - 4.4, center.Y - tipRadius + 13), true, false);
            context.LineTo(new Point(center.X + 4.4, center.Y - tipRadius + 13), true, false);
        }
        arrow.Freeze();

        var arrowUnderlay = new Pen(PointerUnderlayBrush, 2.2) { LineJoin = PenLineJoin.Round };
        arrowUnderlay.Freeze();
        dc.DrawGeometry(InkBrush, arrowUnderlay, arrow);

        var pinRadius = 3.2;
        var pinCenter = new Point(center.X, center.Y - baseRadius);
        dc.DrawEllipse(GoldBrush, inkPen, pinCenter, pinRadius, pinRadius);

        dc.Pop();
    }

    private static Geometry CreateAnnularSector(
        Point center,
        double outerRadius,
        double innerRadius,
        double startAngle,
        double endAngle)
    {
        var span = endAngle - startAngle;
        if (span >= 359.5)
        {
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new EllipseGeometry(center, outerRadius, outerRadius));
            group.Children.Add(new EllipseGeometry(center, innerRadius, innerRadius));
            group.Freeze();
            return group;
        }

        var outerStart = Polar(center, outerRadius, startAngle);
        var outerEnd = Polar(center, outerRadius, endAngle);
        var innerStart = Polar(center, innerRadius, startAngle);
        var innerEnd = Polar(center, innerRadius, endAngle);
        var largeArc = span > 180;

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new ArcSegment(
            outerEnd,
            new Size(outerRadius, outerRadius),
            0,
            largeArc,
            SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(
            innerStart,
            new Size(innerRadius, innerRadius),
            0,
            largeArc,
            SweepDirection.Counterclockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point Polar(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static Brush CreateLinearGradient(Color from, Color to)
    {
        var brush = new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateHatchBrush(Color background, Color stripe)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(background),
            null,
            new RectangleGeometry(new Rect(0, 0, 10, 10))));

        var linePen = new Pen(new SolidColorBrush(stripe), 2.1);
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

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness)
        {
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }
}
