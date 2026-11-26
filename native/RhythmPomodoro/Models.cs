namespace RhythmPomodoro;

public enum SegmentKind
{
    Work,
    Rest
}

public sealed class BreakInterval
{
    public int Start { get; set; }
    public int End { get; set; }

    public BreakInterval()
    {
    }

    public BreakInterval(int start, int end)
    {
        Start = start;
        End = end;
    }

    public int Duration => End - Start;

    public BreakInterval Clone() => new(Start, End);
}

public sealed class CycleSegment
{
    public CycleSegment(SegmentKind kind, double start, double end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public SegmentKind Kind { get; }
    public double Start { get; }
    public double End { get; }
    public double Duration => End - Start;
}

public sealed class CyclePreset
{
    public CyclePreset(string key, string name, int cycleMinutes, int cycles, params (int Start, int End)[] breaks)
    {
        Key = key;
        Name = name;
        CycleMinutes = cycleMinutes;
        Cycles = cycles;
        Breaks = breaks.Select(pair => new BreakInterval(pair.Start, pair.End)).ToList();
    }

    public string Key { get; }
    public string Name { get; }
    public int CycleMinutes { get; }
    public int Cycles { get; }
    public List<BreakInterval> Breaks { get; }
}

public static class ScheduleBuilder
{
    public const int MinCycleMinutes = 5;
    public const int MaxCycleMinutes = 180;
    public const int MinCycles = 1;
    public const int MaxCycles = 24;

    public static List<CycleSegment> Build(int cycleMinutes, IEnumerable<BreakInterval> breaks)
    {
        var ordered = breaks
            .Where(item => item.End > item.Start)
            .Select(item => new BreakInterval(
                Math.Clamp(item.Start, 0, cycleMinutes),
                Math.Clamp(item.End, 0, cycleMinutes)))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .ToList();

        var schedule = new List<CycleSegment>();
        var cursor = 0d;

        foreach (var rest in ordered)
        {
            if (rest.Start > cursor)
            {
                schedule.Add(new CycleSegment(SegmentKind.Work, cursor, rest.Start));
            }

            if (rest.End > rest.Start)
            {
                schedule.Add(new CycleSegment(SegmentKind.Rest, rest.Start, rest.End));
            }

            cursor = Math.Max(cursor, rest.End);
        }

        if (cursor < cycleMinutes)
        {
            schedule.Add(new CycleSegment(SegmentKind.Work, cursor, cycleMinutes));
        }

        return schedule;
    }

    public static CycleSegment? FindSegment(IReadOnlyList<CycleSegment> schedule, double minute)
    {
        foreach (var segment in schedule)
        {
            if (minute >= segment.Start - 0.0001 && minute < segment.End - 0.0001)
            {
                return segment;
            }
        }

        return schedule.Count > 0 ? schedule[^1] : null;
    }

    public static string Signature(int cycleMinutes, int cycles, IEnumerable<BreakInterval> breaks)
    {
        var rest = string.Join(",", breaks
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .Select(item => $"{item.Start}-{item.End}"));
        return $"{cycleMinutes}|{cycles}|{rest}";
    }
}
