using System.Globalization;

namespace CoreSwap.Services;

public static class ScheduleValidator
{
    public static string ToDateString(DateTime? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? throw new ArgumentException("Choose a date.");

    public static bool TryParseDate(string? text, out DateTime date) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    public static string ToTimeString(TimeSpan? time) =>
        time?.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? throw new ArgumentException("Choose a time.");

    public static bool IsFuture(DateTime? date, TimeSpan? time) =>
        date.HasValue && time.HasValue && date.Value.Date.Add(time.Value) > DateTime.Now;
}
