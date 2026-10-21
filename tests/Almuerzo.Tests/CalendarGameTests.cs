using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class CalendarGameTests
{
    [Fact] public void CalendarMatchesTheTwelvePublishedNonWorkingDatesFor2026()
    {
        var dates = DominicanCalendar.Year(2026).Select(x => x.Date).ToArray();
        Assert.Equal(new[] { "2026-01-01", "2026-01-05", "2026-01-21", "2026-01-26", "2026-02-27", "2026-04-03", "2026-05-04", "2026-06-04", "2026-08-16", "2026-09-24", "2026-11-09", "2026-12-25" }, dates);
        Assert.True(BusinessRules.BusinessDay(new DateOnly(2026, 11, 6), []));
        Assert.False(BusinessRules.BusinessDay(new DateOnly(2026, 11, 9), []));
        Assert.Contains(DominicanCalendar.Year(2027), x => x.Date == "2027-03-26" && x.Description == "Viernes Santo");
        var clock = new FixedClock { Current = new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero) };
        Assert.Equal(new DateOnly(2027, 1, 5), new BusinessRules(clock).NextBusinessDay([]));
    }
    [Theory]
    [InlineData(1, 0, 341, 63, 56, 7, 191300, 0, 0)]
    [InlineData(123456, 900, 972, 65, 62, 8, 35780, 0, 200180)]
    [InlineData(2147483646, 299, 740, 67, 58, 8, 194900, 0, 93140)]
    [InlineData(42, 3999, 4294, 76, 148, 5, 191680, 26240, 110420)]
    [InlineData(98765, 6000, 5192, 56, 109, 3, 0, 183440, 0)]
    public void ServerReplayMatchesBrowserVectors(int seed, int score, int final, int hits, int misses, int best, int frenzy, int slow, int shield)
    {
        var moves = Enumerable.Range(0, 800).Select(i => new RushMove(100 + i * 240, (i + 2) % 4)).ToArray();
        var sim = new PotSimulation(seed, score); sim.Advance(200000, moves);
        Assert.Equal((final, hits, misses, best, frenzy, slow, shield), (sim.Score, sim.Hits, sim.Misses, sim.Best, sim.FrenzyUntil, sim.SlowUntil, sim.ShieldUntil));
    }
    [Fact] public void FirstContactSpecialEffectsAndDynamicDifficultyAreDeterministic()
    {
        var sim = new PotSimulation(1, 499);
        void Drop(string kind, int lane = 1) => sim.Drops.Add(new() { Id = 99, Lane = lane, Kind = kind, Progress = 7550 });
        Drop("food"); sim.Advance(0, []); Assert.Equal(509, sim.Score); Assert.Equal(2, sim.Level);
        Drop("shield"); sim.Advance(50, []); Drop("burnt"); sim.Advance(100, []); Assert.Equal(509, sim.Score); Assert.Equal(0, sim.Misses);
        Drop("feast"); sim.Advance(150, []); Drop("burnt", 2); Drop("food"); sim.Advance(200, []); Assert.Equal(539, sim.Score); Assert.DoesNotContain(sim.Drops, x => x.Kind == "burnt");
        sim.Advance(10200, []); sim.Drops.Clear(); var before = sim.Score;
        Drop("burnt"); sim.Advance(10250, []); Assert.Equal(before - 12, sim.Score); Assert.Equal(0, sim.Streak);
    }
    [Fact] public async Task AutomaticHolidayBlocksMenuAndOrdersAndLocalClosuresRemainRegional()
    {
        await using var app = new AppFactory(); await app.Seed();
        using var admin = app.Client(); using var user = app.Client(); await app.Login(admin, "general@infotep.gob.do"); await app.Login(user, "user@infotep.gob.do");
        var calendar = await app.Rpc(admin, "apiGetHolidays"); Assert.Contains(calendar["holidays"]!.AsArray(), x => x!["date"]!.ToString() == "2026-11-09" && x["automatic"]!.GetValue<bool>());
        Assert.False((await app.Rpc(admin, "apiDeleteHoliday", "2026-11-09"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(admin, "apiSaveMenuItem", "2026-11-09", "Arroces", new { plato = "Arroz", hab = "SI" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(user, "apiSubmitOrder", new { date = "2026-11-09", categorias = new[] { "Arroces" }, items = new[] { "Arroz" } }))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(admin, "apiSaveHoliday", "2026-10-14", "Jornada local"))["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Regions.Add(new Region { Id = "new-region", Name = "Otra regional sintética" }); await db.SaveChangesAsync(); db.RegionId = "new-region";
        Assert.DoesNotContain("2026-10-14", await scope.ServiceProvider.GetRequiredService<LunchService>().Holidays());
        Assert.Contains("2026-11-09", await scope.ServiceProvider.GetRequiredService<LunchService>().Holidays());
    }
    [Fact] public async Task ExistingFutureHolidayOrdersAreCancelledOnceWithTransactionalNotification()
    {
        await using var app = new AppFactory(); await app.Seed();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var lunch = scope.ServiceProvider.GetRequiredService<LunchService>();
        db.Orders.AddRange(new Order { Id = "future", Email = "user@infotep.gob.do", Name = "Usuario", DepartmentId = "a", Date = "2026-11-09", Summary = "Arroz" }, new Order { Id = "past", Email = "user@infotep.gob.do", Name = "Usuario", DepartmentId = "a", Date = "2026-01-05", Summary = "Arroz" });
        await db.SaveChangesAsync(); await lunch.ReconcileNationalCalendar(); await lunch.ReconcileNationalCalendar();
        Assert.Equal("CANCELADO", (await db.Orders.FindAsync("future"))!.Status); Assert.Equal("ACTIVO", (await db.Orders.FindAsync("past"))!.Status);
        Assert.Equal(1, await db.Outbox.CountAsync()); Assert.Equal(1, await db.Audit.CountAsync(x => x.Action == "NationalHolidayOrderCancelled"));
    }
    [Fact] public async Task CheckpointRejectsFutureTimeRewrittenHistoryAndImpossibleControls()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client(); await app.Login(c, "user@infotep.gob.do");
        var start = await app.Rpc(c, "apiStartLunchRush", "pot-v4"); var id = start["id"]!.ToString();
        Assert.False((await app.Rpc(c, "apiFinishLunchRush", new { id, elapsedMs = 15000, moves = Array.Empty<object>() }))["ok"]!.GetValue<bool>());
        app.Clock.Current = app.Clock.Current.AddSeconds(10);
        var moves = new[] { new { t = 100, lane = 0 }, new { t = 200, lane = 3 } }; // includes left-edge wrap
        var saved = await app.Rpc(c, "apiCheckpointLunchRush", new { id, elapsedMs = 10000, moves }); Assert.True(saved["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiCheckpointLunchRush", new { id, elapsedMs = 10000, moves = Array.Empty<object>() }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiFinishLunchRush", new { id, elapsedMs = 10000, moves = new[] { new { t = 100, lane = 3 } } }))["ok"]!.GetValue<bool>());
        app.Clock.Current = app.Clock.Current.AddSeconds(3);
        var finish = await app.Rpc(c, "apiFinishLunchRush", new { id, elapsedMs = 13000, moves }); Assert.True(finish["ok"]!.GetValue<bool>()); Assert.Equal(887, finish["state"]!["remainingSeconds"]!.GetValue<int>());
    }
    [Fact] public async Task RankingIsNationalByDefaultAndProjectsOnlyPublicGameFields()
    {
        await using var app = new AppFactory(); await app.Seed();
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.AllowCrossRegionWrites = true;
            db.Regions.Add(new Region { Id = "east", Name = "Este", Alias = "Regional Este" });
            db.Departments.Add(new Department { Id = "east-group", RegionId = "east", Name = "Grupo Este" });
            db.Users.Add(new AppUser { Email = "national@infotep.gob.do", EmployeeCode = "9998", Name = "Líder sintético", RegionId = "east", DepartmentId = "east-group", Status = "ACTIVO", GameJson = "{\"gameVersion\":\"pot-v2\",\"monthKey\":\"2026-10\",\"score\":999}" });
            db.Users.Add(new AppUser { Email = "blocked@infotep.gob.do", EmployeeCode = "9999", Name = "Oculto sintético", RegionId = "east", DepartmentId = "east-group", Status = "ACTIVO", RegionalBlocked = true });
            await db.SaveChangesAsync();
        }
        using var c = app.Client(); await app.Login(c, "user@infotep.gob.do");
        var all = await app.Rpc(c, "apiGetChefGameRanking", ""); Assert.Equal("NATIONAL", all["scope"]!.ToString()); Assert.Equal("Líder sintético", all["rows"]![0]!["nombre"]!.ToString());
        Assert.Contains("Regional Este", all.ToJsonString()); Assert.DoesNotContain("@", all.ToJsonString()); Assert.DoesNotContain("9998", all.ToJsonString()); Assert.DoesNotContain("Oculto sintético", all.ToJsonString());
        var regional = await app.Rpc(c, "apiGetChefGameRanking", "", "REGIONAL"); Assert.DoesNotContain("Líder sintético", regional.ToJsonString()); Assert.Equal(4, regional["total"]!.GetValue<int>());
        Assert.False((await app.Rpc(c, "apiGetChefGameRanking", "", "INVALID"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(c, "apiGetChefGameRanking", "", "NATIONAL", -1))["ok"]!.GetValue<bool>());
    }
    [Fact] public async Task SectionSettingsAreAtomicAndRejectAnIncoherentSchedule()
    {
        await using var app = new AppFactory(); await app.Seed(); using var admin = app.Client(); await app.Login(admin, "general@infotep.gob.do");
        var bad = await app.Rpc(admin, "apiSaveConfig", new Dictionary<string,string> { ["APP_TITLE"] = "No persistir", ["HORA_ENVIO"] = "01:00", ["MINUTOS_PREV_CIERRE"] = "120" });
        Assert.False(bad["ok"]!.GetValue<bool>());
        var good = await app.Rpc(admin, "apiSaveConfig", new Dictionary<string,string> { ["HORA_ENVIO"] = "16:00", ["MINUTOS_PREV_CIERRE"] = "30", ["HORA_RECORDATORIO"] = "14:00" });
        Assert.True(good["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Solicitud de Almuerzo", (await db.Settings.FindAsync(db.RegionId, "APP_TITLE"))!.Value);
        Assert.Equal("Comedor Institucional", (await db.Settings.FindAsync(db.RegionId, "MAIL_SENDER_NAME"))!.Value);
        Assert.Equal("16:00", (await db.Settings.FindAsync(db.RegionId, "HORA_ENVIO"))!.Value);
    }
}
