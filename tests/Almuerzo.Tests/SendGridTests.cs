using System.Net;
using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Almuerzo.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Almuerzo.Tests;

public class SendGridTests
{
    private class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => callback(request);
    }
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Mail:Mode"] = "SendGrid", ["Mail:From"] = "sender@example.test", ["Mail:AllowSendGridInDevelopment"] = "true", ["SendGrid:ApiKey"] = "synthetic-api-key"
    }).Build();
    [Fact] public async Task SendGridUsesBearerSingleTestRecipientHtmlAndBase64Attachments()
    {
        var config = Configuration(); JsonObject? payload = null;
        var delivery = new SendGridDelivery(new HttpClient(new Handler(async request => {
            Assert.Equal("https://api.sendgrid.com/v3/mail/send", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("synthetic-api-key", request.Headers.Authorization.Parameter);
            payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject(); return new(HttpStatusCode.Accepted);
        })), config);
        await using var app = new AppFactory(); await app.Seed();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var paths = scope.ServiceProvider.GetRequiredService<StoragePaths>(); Directory.CreateDirectory(paths.Reports);
        var attachment = Path.Combine(paths.Reports, "synthetic.pdf"); await File.WriteAllBytesAsync(attachment, "%PDF synthetic"u8.ToArray());
        var record = new MailMessageRecord { Recipient = "actual@example.test", Subject = "Prueba", Body = "Contenido seguro", AttachmentPath = attachment }; db.Outbox.Add(record); await db.SaveChangesAsync();
        var policy = new MailDeliveryPolicy(config, new TestEnvironment());
        await new OutboxDelivery(db, paths, policy, config, NullLogger<OutboxDelivery>.Instance, delivery).DeliverAsync(new() { ["TEST_EMAIL_MODE"] = "TRUE", ["TEST_EMAIL_DEST"] = "test@example.test" }, default);
        Assert.NotNull(record.SentAt); Assert.Equal("test@example.test", payload!["personalizations"]![0]!["to"]![0]!["email"]!.ToString());
        Assert.DoesNotContain("actual@example.test", payload.ToJsonString());
        Assert.Equal("text/html", payload["content"]![1]!["type"]!.ToString());
        Assert.Equal(Convert.ToBase64String("%PDF synthetic"u8.ToArray()), payload["attachments"]![0]!["content"]!.ToString());
    }
    [Fact] public async Task SendGridThrottleKeepsMessagePendingWithRetryDelay()
    {
        var config = Configuration(); var handler = new Handler(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromMinutes(5)); return Task.FromResult(r); });
        await using var app = new AppFactory(); await app.Seed(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var paths = scope.ServiceProvider.GetRequiredService<StoragePaths>();
        var record = new MailMessageRecord { Recipient = "actual@example.test", Subject = "Prueba", Body = "Texto" }; db.Outbox.Add(record); await db.SaveChangesAsync();
        var service = new OutboxDelivery(db, paths, new MailDeliveryPolicy(config, new TestEnvironment()), config, NullLogger<OutboxDelivery>.Instance, new SendGridDelivery(new HttpClient(handler), config));
        await service.DeliverAsync(new() { ["TEST_EMAIL_MODE"] = "TRUE", ["TEST_EMAIL_DEST"] = "test@example.test" }, default);
        Assert.Null(record.SentAt); Assert.Equal(1, record.Attempts); Assert.True(record.NextAttemptUtcTicks > DateTimeOffset.UtcNow.AddMinutes(4).UtcTicks);
    }
    [Theory]
    [InlineData("FALSE", "test@example.test")]
    [InlineData("TRUE", "")]
    [InlineData("TRUE", "a@example.test,b@example.test")]
    public void SendGridDevelopmentCannotDeliverOutsideOneTestInbox(string mode, string destination)
    {
        var policy = new MailDeliveryPolicy(Configuration(), new TestEnvironment());
        Assert.Null(policy.Resolve(new Dictionary<string,string> { ["TEST_EMAIL_MODE"] = mode, ["TEST_EMAIL_DEST"] = destination }, "actual@example.test"));
    }
}
