using System.Globalization;
using System.Text.RegularExpressions;

namespace Almuerzo.Web.Services;

public class BusinessRules(TimeProvider clock)
{
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"));
    public string Today => Now.ToString("yyyy-MM-dd");
    public static DateOnly Date(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new BusinessException("Fecha inválida. Usa YYYY-MM-DD.");
    public static bool BusinessDay(DateOnly date, HashSet<string> holidays) => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(date.ToString("yyyy-MM-dd"));
    public DateOnly NextBusinessDay(HashSet<string> holidays)
    {
        var day = DateOnly.FromDateTime(Now.DateTime).AddDays(1);
        while (!BusinessDay(day, holidays)) day = day.AddDays(1);
        return day;
    }
    public bool OrderingOpen(string value, HashSet<string> holidays, string sendTime = "15:00", int minutesBefore = 30)
    {
        var target = Date(value);
        var today = DateOnly.FromDateTime(Now.DateTime);
        if (target <= today || !BusinessDay(target, holidays)) return false;
        var previous = target.AddDays(-1);
        while (!BusinessDay(previous, holidays)) previous = previous.AddDays(-1);
        if (today > previous) return false;
        var time = TimeOnly.TryParseExact(sendTime, "HH:mm", out var parsed) ? parsed : new TimeOnly(15, 0);
        return today < previous || Now.DateTime <= previous.ToDateTime(time).AddMinutes(-minutesBefore);
    }
    public void MenuDate(string value, HashSet<string> holidays)
    {
        if (Date(value) <= DateOnly.FromDateTime(Now.DateTime) || !BusinessDay(Date(value), holidays))
            throw new BusinessException("Solo puedes gestionar menú para días laborables futuros.");
    }
    public bool RatingAllowed(string value) => Date(value) < DateOnly.FromDateTime(Now.DateTime) || value == Today && Now.Hour >= 12;
    public static string Text(string value)
    {
        var minor = new HashSet<string>(["de", "del", "la", "las", "el", "los", "y", "e", "o", "u", "con", "al", "en"]);
        return string.Join(' ', Regex.Replace(value.Trim(), @"\s+", " ").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => i > 0 && minor.Contains(w) ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}

public class BusinessException(string message) : Exception(message);
