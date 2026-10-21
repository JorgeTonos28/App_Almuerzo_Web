using System.Collections.Concurrent;

namespace Almuerzo.Web.Services;

public record CalendarDay(string Date, string Description, bool Automatic);

// Non-working dates, including the observed Monday, rather than commemorative dates.
// Source and annual review procedure: docs/CALENDAR-AND-GAME.md.
public static class DominicanCalendar
{
    private static readonly ConcurrentDictionary<int, CalendarDay[]> Cache = new();
    public static CalendarDay[] Year(int year) => Cache.GetOrAdd(year, Build);
    public static CalendarDay[] Around(int year) => Enumerable.Range(Math.Max(1, year - 1), year == 1 ? 2 : 3).SelectMany(Year).ToArray();
    private static CalendarDay[] Build(int year)
    {
        var days = new List<CalendarDay>();
        void Add(int month, int day, string name, bool movable = false) {
            var date = new DateOnly(year, month, day);
            if (movable) date = date.AddDays(date.DayOfWeek switch {
                DayOfWeek.Tuesday => -1, DayOfWeek.Wednesday => -2,
                DayOfWeek.Thursday => 4, DayOfWeek.Friday => 3, _ => 0
            });
            days.Add(new(date.ToString("yyyy-MM-dd"), name, true));
        }
        Add(1, 1, "Año Nuevo"); Add(1, 6, "Día de los Santos Reyes", true);
        Add(1, 21, "Nuestra Señora de la Altagracia"); Add(1, 26, "Natalicio de Juan Pablo Duarte", true);
        Add(2, 27, "Independencia Nacional"); Add(5, 1, "Día del Trabajo", true);
        Add(8, 16, "Restauración de la República"); Add(9, 24, "Nuestra Señora de las Mercedes");
        Add(11, 6, "Día de la Constitución", true); Add(12, 25, "Navidad");
        // Gregorian computus: Easter determines Good Friday and Corpus Christi.
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4,
            f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30,
            i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7,
            m = (a + 11 * h + 22 * l) / 451, n = h + l - 7 * m + 114;
        var easter = new DateOnly(year, n / 31, n % 31 + 1);
        days.Add(new(easter.AddDays(-2).ToString("yyyy-MM-dd"), "Viernes Santo", true));
        days.Add(new(easter.AddDays(60).ToString("yyyy-MM-dd"), "Corpus Christi", true));
        return days.OrderBy(x => x.Date).ToArray();
    }
}
