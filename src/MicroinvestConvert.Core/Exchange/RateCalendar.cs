namespace MicroinvestConvert.Core.Exchange;

/// <summary>Days one after the other, both ends included.</summary>
public readonly record struct DayRange(DateTime From, DateTime To)
{
    public int Days => (To - From).Days + 1;
}

/// <summary>Which days have to be asked for, and which answers are for good.</summary>
public static class RateCalendar
{
    /// <summary>The tables of the source are read from here on.</summary>
    public static readonly DateTime Earliest = new(2000, 1, 1);

    public static bool IsWeekend(DateTime day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static IEnumerable<DateTime> Days(DateTime from, DateTime to)
    {
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1)) yield return day;
    }

    /// <summary>
    /// The days to ask the source for, so that all of <paramref name="wanted"/> become known. No
    /// table is published on a weekend: a Saturday and a Sunday have the one of the day before
    /// them, and are asked for only when that day is neither known nor wanted.
    /// </summary>
    public static List<DateTime> ToAsk(IReadOnlySet<DateTime> wanted, IReadOnlySet<DateTime> known) => wanted
        .Where(day => !known.Contains(day))
        .Where(day => !IsWeekend(day) || !(known.Contains(day.AddDays(-1)) || wanted.Contains(day.AddDays(-1))))
        .Order()
        .ToList();

    /// <summary>
    /// Will the answer for a day stay? A table of an earlier day is the last word only when there
    /// is a later one already – then the day has none of its own – or on a weekend after a Friday.
    /// Otherwise the table of the day may still be coming.
    /// </summary>
    /// <param name="day">The day that was asked for.</param>
    /// <param name="published">The date of the table it got.</param>
    /// <param name="latest">The newest table there is.</param>
    public static bool IsFinal(DateTime day, DateTime published, DateTime latest) =>
        published == day || day <= latest || (IsWeekend(day) && published == FridayBefore(day));

    private static DateTime FridayBefore(DateTime weekend) => weekend.AddDays(weekend.DayOfWeek == DayOfWeek.Saturday ? -1 : -2);

    /// <summary>The days as a few periods.</summary>
    public static IReadOnlyList<DayRange> Ranges(IEnumerable<DateTime> days)
    {
        var ranges = new List<DayRange>();
        foreach (var day in days.Distinct().Order())
        {
            if (ranges.Count > 0 && ranges[^1].To.AddDays(1) == day) ranges[^1] = ranges[^1] with { To = day };
            else ranges.Add(new DayRange(day, day));
        }

        return ranges;
    }
}
