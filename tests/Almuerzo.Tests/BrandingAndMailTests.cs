using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class BrandingAndMailTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNQTX79HwAElwJzVt2vfAAAAABJRU5ErkJggg==");
    private static MultipartFormDataContent Form(string text, byte[]? icon = null, byte[]? stamp = null, bool reset = false, bool remove = false)
    {
        var form = new MultipartFormDataContent { { new StringContent(text), "footerText" }, { new StringContent(reset.ToString()), "resetFavicon" }, { new StringContent(remove.ToString()), "removeStamp" } };
        if (icon != null) form.Add(new ByteArrayContent(icon), "favicon", "icon.png");
        if (stamp != null) form.Add(new ByteArrayContent(stamp), "stamp", "stamp.png");
        return form;
    }
    private static async Task<HttpResponseMessage> Save(HttpClient client, MultipartFormDataContent form, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/branding") { Content = form };
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", AppFactory.RpcToken(await client.GetStringAsync("/")));
        return await client.SendAsync(request);
    }
    [Fact] public async Task BrandingRequiresGeneralAdminAndCsrf()
    {
        await using var app = new AppFactory(); await app.Seed(); using var user = app.Client(); using var admin = app.Client();
        await app.Login(user, "admin@infotep.gob.do"); await app.Login(admin, "general@infotep.gob.do");
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(user, Form("Forged", Png))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(admin, Form("Forged", Png), false)).StatusCode);
        Assert.False((await app.Rpc(admin, "apiSaveConfig", new Dictionary<string, string> { ["FAVICON_ASSET_ID"] = "forged" }))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(admin, "apiSaveConfig", new Dictionary<string, string> { ["FOOTER_TEXT"] = "forged" }))["ok"]!.GetValue<bool>());
    }
    [Fact] public async Task BrandingPersistsAndImagesCanBeResetWithoutServingOldAssets()
    {
        await using var app = new AppFactory(); await app.Seed(); using var admin = app.Client(); await app.Login(admin, "general@infotep.gob.do");
        var response = await Save(admin, Form("Dirección de Prueba © 2026 <script>alert(1)</script>", Png, Png)); response.EnsureSuccessStatusCode();
        var settings = (await response.Content.ReadFromJsonAsync<JsonObject>())!["branding"]!;
        var icon = settings["faviconUrl"]!.ToString(); var stamp = settings["footerSignatureUrl"]!.ToString();
        using var anonymous = app.Client(); var image = await anonymous.GetAsync(icon); Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType); Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());
        var login = await anonymous.GetStringAsync("/Auth/Login"); Assert.Contains(icon, login); Assert.Contains(stamp, login); Assert.Contains("&lt;script&gt;", login); Assert.DoesNotContain("<script>alert(1)</script>", login);
        var reset = await Save(admin, Form("Nuevo texto", reset: true, remove: true)); reset.EnsureSuccessStatusCode();
        var after = (await reset.Content.ReadFromJsonAsync<JsonObject>())!["branding"]!; Assert.Equal("/assets/favicon.svg", after["faviconUrl"]!.ToString()); Assert.Empty(after["footerSignatureUrl"]!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(icon)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(stamp)).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(0, await db.BrandingAssets.CountAsync());
        Assert.Equal(2, await db.Audit.CountAsync(x => x.Action == "SaveBranding"));
    }
    [Theory]
    [InlineData("crc")]
    [InlineData("trailing")]
    [InlineData("svg")]
    [InlineData("oversized")]
    [InlineData("dimensions")]
    [InlineData("raster")]
    public async Task InvalidImagesDoNotChangeFooterOrAssets(string kind)
    {
        await using var app = new AppFactory(); await app.Seed(); using var admin = app.Client(); await app.Login(admin, "general@infotep.gob.do");
        var bytes = Png.ToArray();
        if (kind == "crc") bytes[32] ^= 1;
        if (kind == "trailing") bytes = bytes.Concat("<script>"u8.ToArray()).ToArray();
        if (kind == "svg") bytes = "<svg onload='alert(1)'/>"u8.ToArray();
        if (kind == "oversized") bytes = new byte[512_001];
        if (kind == "dimensions") bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAgEAAAABCAYAAABHeX1IAAAAHUlEQVR4nGNUTX79n2EUjIJRMApGwSgYBQwjDQAApbkCdA6XT4QAAAAASUVORK5CYII=");
        if (kind == "raster") bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADElEQVR4nGNgoAMAAABlAAF/+ogNAAAAAElFTkSuQmCC");
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(admin, Form("Should roll back", bytes))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal("Dirección de Innovación © 2025", (await db.Settings.FindAsync("FOOTER_TEXT"))!.Value); Assert.Equal(0, await db.BrandingAssets.CountAsync());
    }
    [Fact] public async Task AccessRequestUsesDirectoryCodeEvenIfFormIsForged()
    {
        await using var app = new AppFactory(); await app.Seed(); using var user = app.Client(); await app.Login(user, "new@infotep.gob.do");
        var html = await user.GetStringAsync("/Auth/Denied"); Assert.Contains("1004", html); Assert.DoesNotContain("name=\"Code\"", html);
        await user.PostAsync("/Auth/RequestAccess", new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = "1000", ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal("1004", (await db.Users.FindAsync("new@infotep.gob.do"))!.EmployeeCode);
    }
    [Fact] public async Task DirectoryCodeConflictCannotUseImportedPrivileges()
    {
        await using var app = new AppFactory(); await app.Seed();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.FindAsync("general@infotep.gob.do"))!.EmployeeCode = "9999"; await db.SaveChangesAsync(); }
        using var user = app.Client(); var html = await user.GetStringAsync("/Auth/Login");
        var response = await user.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = "general@infotep.gob.do", ["Password"] = "test-only", ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("revisión administrativa", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync())); Assert.Equal(HttpStatusCode.Redirect, (await user.GetAsync("/")).StatusCode);
    }
    [Theory]
    [InlineData("Development", "Smtp", false, "FALSE", "test@example.test", true)]
    [InlineData("Development", "Smtp", true, "TRUE", "test@example.test", false)]
    [InlineData("Development", "Smtp", true, "FALSE", "test@example.test", null)]
    [InlineData("Development", "Smtp", true, "TRUE", "", null)]
    [InlineData("Development", "Smtp", true, "TRUE", "first@example.test,second@example.test", null)]
    [InlineData("Production", "Smtp", false, "TRUE", "", null)]
    [InlineData("Production", "Smtp", false, "TRUE", "test@example.test", false)]
    [InlineData("Production", "Smtp", false, "FALSE", "", false)]
    [InlineData("Production", "Unknown", false, "FALSE", "", null)]
    public void SmtpTestingCannotDeliverToRealAudience(string environment, string mode, bool allow, string test, string destination, bool? pickup)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mail:Mode"] = mode, ["Mail:Host"] = "smtp.example.test", ["Mail:AllowSmtpInDevelopment"] = allow.ToString() }).Build();
        var policy = new MailDeliveryPolicy(config, new TestEnvironment { EnvironmentName = environment });
        var route = policy.Resolve(new Dictionary<string, string> { ["TEST_EMAIL_MODE"] = test, ["TEST_EMAIL_DEST"] = destination }, "actual@example.test");
        Assert.Equal(pickup, route?.Pickup);
        if (route is { Pickup: false }) Assert.Equal(test == "TRUE" ? destination : "actual@example.test", route.Destination);
    }
    [Fact] public async Task PickupProcessesQueuedAccessNotificationWithoutExternalDelivery()
    {
        await using var app = new AppFactory(); await app.Seed(); using var user = app.Client(); await app.Login(user, "new@infotep.gob.do"); var html = await user.GetStringAsync("/Auth/Denied");
        await user.PostAsync("/Auth/RequestAccess", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = AppFactory.Token(html) }));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = await scope.ServiceProvider.GetRequiredService<LunchService>().Config(); config["TEST_EMAIL_DEST"] = "test@example.test";
        await scope.ServiceProvider.GetRequiredService<OutboxDelivery>().DeliverAsync(config, default);
        Assert.Equal(2, await db.Outbox.CountAsync(x => x.SentAt != null));
        var files = Directory.GetFiles(Path.Combine(app.Storage, "mail"), "*.eml"); Assert.Equal(2, files.Length);
        foreach (var file in files) { var mail = await File.ReadAllTextAsync(file); Assert.Contains("To: test@example.test", mail); Assert.DoesNotContain("To: general@", mail); Assert.Contains("text/html", mail); }
    }
    [Fact] public async Task TestCloseIncludesDepartmentReportsAndOnlyUsesTestRecipient()
    {
        await using var app = new AppFactory(); await app.Seed(); using var admin = app.Client(); await app.Login(admin, "general@infotep.gob.do");
        var config = await app.Rpc(admin, "apiSaveConfig", new Dictionary<string, string> { ["TEST_EMAIL_MODE"] = "TRUE", ["TEST_EMAIL_DEST"] = "test@example.test" }); Assert.True(config["ok"]!.GetValue<bool>(), config["msg"]?.ToString());
        Assert.True((await app.Rpc(admin, "apiSendDailyCloseEmailsTest"))["ok"]!.GetValue<bool>());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var records = await db.Outbox.ToListAsync();
        Assert.Equal(2, records.Count); Assert.All(records, x => { Assert.Equal("test@example.test", x.Recipient); Assert.True(File.Exists(x.AttachmentPath)); Assert.EndsWith(".xlsx", x.AttachmentPath); Assert.EndsWith(".pdf", JsonNode.Parse(x.ExtraAttachmentsJson)![0]!.ToString()); });
        Assert.NotEqual(records[0].AttachmentPath, records[1].AttachmentPath); Assert.Empty(await db.JobRuns.ToListAsync());
    }
}
