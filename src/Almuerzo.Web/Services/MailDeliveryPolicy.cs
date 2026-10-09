using System.Net.Mail;

namespace Almuerzo.Web.Services;

public record MailRoute(bool Pickup, string Destination);
public class MailDeliveryPolicy(IConfiguration config, IWebHostEnvironment env)
{
    public MailRoute? Resolve(IReadOnlyDictionary<string, string> settings, string recipient)
    {
        var mode = config["Mail:Mode"] ?? "Pickup";
        if (mode is not ("Pickup" or "Smtp")) return null;
        var pickup = mode == "Pickup" || env.IsDevelopment() && !config.GetValue<bool>("Mail:AllowSmtpInDevelopment");
        var test = settings.GetValueOrDefault("TEST_EMAIL_MODE", "TRUE") == "TRUE";
        var destination = test ? settings.GetValueOrDefault("TEST_EMAIL_DEST", "").Trim() : recipient;
        if (pickup) return new(true, destination.Length == 0 ? recipient : destination);
        if (env.IsDevelopment() && !test || string.IsNullOrWhiteSpace(config["Mail:Host"]) || !MailAddress.TryCreate(destination, out var address) || address.Address != destination || destination.Contains('\r') || destination.Contains('\n')) return null;
        return new(false, destination);
    }
}
