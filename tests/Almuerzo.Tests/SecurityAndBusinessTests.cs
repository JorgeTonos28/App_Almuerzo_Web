using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class SecurityAndBusinessTests
{
    [Fact] public async Task AnonymousAndMissingCsrfCannotReadOrMutate()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client();
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);
        await app.Login(c, "user@infotep.gob.do");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/rpc", new { method = "apiGetAdminData", args = Array.Empty<object>() })).StatusCode);
        var denied = await app.Rpc(c, "apiGetAdminData"); Assert.False(denied["ok"]!.GetValue<bool>());
        Assert.Contains("Permiso", denied["msg"]!.ToString());
    }
    [Fact] public async Task DirectoryIdentityAloneDoesNotGrantAccessAndRequestsArePending()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client();
        Assert.Equal("/Auth/Denied", await app.Login(c, "new@infotep.gob.do"));
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);
        var html = await c.GetStringAsync("/Auth/Denied");
        var response = await c.PostAsync("/Auth/RequestAccess", new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = "1004", ["SuggestedDepartment"] = "Departamento A", ["__RequestVerificationToken"] = AppFactory.Token(html) })); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var u = await db.Users.FindAsync("new@infotep.gob.do");
        Assert.Equal("PENDIENTE", u!.Status); Assert.Null(u.DepartmentId); Assert.Equal(Roles.User, u.Role); Assert.Equal(2, await db.Outbox.CountAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/rpc", new { method = "apiGetInitData", args = Array.Empty<object>() })).StatusCode);
    }
    [Fact] public async Task DepartmentScopeBlocksImpersonationPrivilegeEscalationAndForeignOrders()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client(); await app.Login(c, "admin@infotep.gob.do");
        var foreign = await app.Rpc(c, "apiGetInitData", "2026-10-12", "other@infotep.gob.do"); Assert.False(foreign["ok"]!.GetValue<bool>());
        var escalation = await app.Rpc(c, "apiAdminSaveUser", new { email = "user@infotep.gob.do", nombre = "Usuario", departamento = "a", codigo = "1002", rol = "ADMIN_GEN", estado = "ACTIVO" }); Assert.False(escalation["ok"]!.GetValue<bool>());
        var move = await app.Rpc(c, "apiAdminSaveUser", new { email = "other@infotep.gob.do", nombre = "Usuario", departamento = "a", codigo = "1003", rol = "USER", estado = "ACTIVO" }); Assert.False(move["ok"]!.GetValue<bool>());
        var data = await app.Rpc(c, "apiGetAdminData"); Assert.DoesNotContain("other@infotep.gob.do", data.ToJsonString());
    }
    [Fact] public async Task SessionRevocationTakesEffectOnNextRequest()
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client(); await app.Login(c, "general@infotep.gob.do");
        var html = await c.GetStringAsync("/"); Assert.Contains("Almuerzo", html);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var u = await db.Users.FindAsync("general@infotep.gob.do"); u!.Status = "INACTIVO"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/")).StatusCode);
    }
    [Fact] public async Task OrderValidationAndMenuReplacementPreserveUnchangedOrders()
    {
        await using var app = new AppFactory(); await app.Seed(); using var user = app.Client(); using var admin = app.Client(); await app.Login(user, "user@infotep.gob.do"); await app.Login(admin, "general@infotep.gob.do");
        var forged = await app.Rpc(user, "apiSubmitOrder", new { date = "2026-10-12", categorias = new[] { "Arroces" }, items = new[] { "No existe" } }); Assert.False(forged["ok"]!.GetValue<bool>());
        var saved = await app.Rpc(user, "apiSubmitOrder", new { date = "2026-10-12", categorias = new[] { "Arroces" }, items = new[] { "Arroz Blanco" }, comentarios = "" }); Assert.True(saved["ok"]!.GetValue<bool>());
        var orderId = saved["order"]!["id"]!.ToString();
        var badId = await app.Rpc(user, "apiSubmitOrder", new { date = "2026-10-12", orderId = "foreign-id", categorias = new[] { "Arroces" }, items = new[] { "Arroz Blanco" } }); Assert.False(badId["ok"]!.GetValue<bool>());
        var same = await app.Rpc(admin, "apiSaveWeeklyMenu", new Dictionary<string, object> { ["2026-10-12"] = new[] { new { cat = "Arroces", plato = "Arroz Blanco", desc = "" }, new { cat = "Carnes", plato = "Pollo", desc = "" } } }); Assert.Equal(0, same["cancellations"]!.GetValue<int>());
        var changed = await app.Rpc(admin, "apiSaveWeeklyMenu", new Dictionary<string, object> { ["2026-10-12"] = new[] { new { cat = "Arroces", plato = "Arroz Integral", desc = "" } } }); Assert.Equal(1, changed["cancellations"]!.GetValue<int>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal("CANCELADO", (await db.Orders.FindAsync(orderId))!.Status); Assert.Equal(1, await db.Outbox.CountAsync());
    }
    [Fact] public async Task HolidayCancellationAndProviderHistoryAreAtomic()
    {
        await using var app = new AppFactory(); await app.Seed(); using var user = app.Client(); using var admin = app.Client(); await app.Login(user, "user@infotep.gob.do"); await app.Login(admin, "general@infotep.gob.do");
        Assert.True((await app.Rpc(user, "apiSubmitOrder", new { date = "2026-10-12", categorias = new[] { "Arroces" }, items = new[] { "Arroz Blanco" } }))["ok"]!.GetValue<bool>());
        var result = await app.Rpc(admin, "apiSaveHoliday", "2026-10-12", "Suspensión"); Assert.Equal(1, result["cancellations"]!.GetValue<int>());
        Assert.False((await app.Rpc(user, "apiSubmitOrder", new { date = "2026-10-12", categorias = new[] { "Arroces" }, items = new[] { "Arroz Blanco" } }))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(user, "apiSubmitProviderRating", new { rating = 4, comment = "Buena comida" }))["ok"]!.GetValue<bool>());
        Assert.True((await app.Rpc(user, "apiSubmitProviderRating", new { rating = 5, comment = "Mejoró" }))["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(1, await db.Ratings.CountAsync()); Assert.Equal(2, await db.RatingHistory.CountAsync());
    }
    [Fact] public async Task ApprovalRequiresGeneralAdminAndExistingSessionGainsAccessOnlyAfterAssignment()
    {
        await using var app = new AppFactory(); await app.Seed(); using var newcomer = app.Client(); using var admin = app.Client();
        await app.Login(newcomer, "new@infotep.gob.do"); var html = await newcomer.GetStringAsync("/Auth/Denied");
        await newcomer.PostAsync("/Auth/RequestAccess", new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = "1004", ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        await app.Login(admin, "general@infotep.gob.do");
        var approved = await app.Rpc(admin, "apiAdminSaveUser", new { email = "new@infotep.gob.do", nombre = "Nueva cuenta", codigo = "1004", departamento = "a", rol = "USER", estado = "ACTIVO" }); Assert.True(approved["ok"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await newcomer.GetAsync("/")).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(3, await db.Outbox.CountAsync());
    }
    [Fact] public async Task InactiveAccountCanRequestReviewWithoutGainingAccess()
    {
        await using var app = new AppFactory(); await app.Seed();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.FindAsync("user@infotep.gob.do"))!.Status = "INACTIVO"; await db.SaveChangesAsync(); }
        using var user = app.Client(); Assert.Equal("/Auth/Denied", await app.Login(user, "user@infotep.gob.do")); var html = await user.GetStringAsync("/Auth/Denied"); Assert.Contains("Solicitar acceso", html);
        await user.PostAsync("/Auth/RequestAccess", new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = "1002", ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        using var check = app.Services.CreateScope(); var dbCheck = check.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal("PENDIENTE", (await dbCheck.Users.FindAsync("user@infotep.gob.do"))!.Status); Assert.Equal(2, await dbCheck.Outbox.CountAsync()); Assert.Equal(HttpStatusCode.Redirect, (await user.GetAsync("/")).StatusCode);
    }
    [Fact] public async Task PreferencesMergeAndPriceHistoryCannotBeOverwrittenByClient()
    {
        await using var app = new AppFactory(); await app.Seed(); using var user = app.Client(); using var admin = app.Client(); await app.Login(user, "user@infotep.gob.do"); await app.Login(admin, "general@infotep.gob.do");
        Assert.True((await app.Rpc(user, "apiSetUserPreference", "custom", 42))["ok"]!.GetValue<bool>()); Assert.True((await app.Rpc(user, "apiSetUserPreference", "reminders", false))["ok"]!.GetValue<bool>());
        var saved = await app.Rpc(admin, "apiSaveConfig", new Dictionary<string, string> { ["MEAL_PRICE_CURRENT"] = "60", ["MEAL_PRICE_HISTORY_JSON"] = "[]", ["ANNOUNCEMENT_PAYLOAD_JSON"] = "{\"slides\":[]}" }); Assert.True(saved["ok"]!.GetValue<bool>(), saved["msg"]?.ToString());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var prefs = LunchService.Obj((await db.Users.FindAsync("user@infotep.gob.do"))!.PreferencesJson); Assert.Equal(42, prefs["custom"]!.GetValue<int>()); Assert.False(prefs["reminders"]!.GetValue<bool>());
        var history = JsonNode.Parse((await db.Settings.FindAsync("MEAL_PRICE_HISTORY_JSON"))!.Value)!.AsArray(); Assert.Equal(2, history.Count); Assert.Equal("2026-10-09", history[1]!["from"]!.ToString());
    }
    [Fact] public void MailContentIsEncodedAndUnsafeLinksAreOmitted()
    {
        var html = MailTemplate.Html(new MailMessageRecord { Subject = "<script>alert(1)</script>", Body = "<img src=x onerror=alert(1)>" }, new Dictionary<string, string> { ["APP_URL"] = "javascript:alert(1)", ["APP_TITLE"] = "Almuerzo" });
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<img src=x", html); Assert.DoesNotContain("javascript:", html); Assert.Contains("&lt;img", html);
    }
    [Fact] public void BusinessCalendarAndNoonBoundariesUseSantoDomingo()
    {
        var clock = new FixedClock(); var rules = new BusinessRules(clock); var holidays = new HashSet<string>();
        Assert.True(rules.OrderingOpen("2026-10-12", holidays)); Assert.False(rules.OrderingOpen("2026-10-10", holidays));
        clock.Current = new DateTimeOffset(2026, 10, 9, 18, 30, 1, TimeSpan.Zero); Assert.False(rules.OrderingOpen("2026-10-12", holidays));
        Assert.True(rules.OrderingOpen("2026-10-13", holidays)); holidays.Add("2026-10-12"); Assert.False(rules.OrderingOpen("2026-10-13", holidays));
        clock.Current = new DateTimeOffset(2026, 10, 9, 15, 59, 59, TimeSpan.Zero); Assert.False(rules.RatingAllowed("2026-10-09"));
        clock.Current = new DateTimeOffset(2026, 10, 9, 16, 0, 0, TimeSpan.Zero); Assert.True(rules.RatingAllowed("2026-10-09")); Assert.True(rules.RatingAllowed("2026-10-08")); Assert.False(rules.RatingAllowed("2026-10-12"));
    }
}
