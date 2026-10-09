using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;

namespace Almuerzo.Web.Services;

public record DirectoryIdentity(string Email, string Name);
public interface IDirectoryIdentityProvider { Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct); }

public class DirectoryIdentityProvider(HttpClient http, IConfiguration config, IWebHostEnvironment env) : IDirectoryIdentityProvider
{
    public async Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        // Synthetic identity for isolated Development testing. Never accepts arbitrary imported users.
        if (env.IsDevelopment() && config.GetValue<bool>("DevelopmentIdentity:Enabled")) {
            var email = config["DevelopmentIdentity:Email"] ?? ""; var hash = config["DevelopmentIdentity:PasswordHash"] ?? "";
            if (username.Equals(email, StringComparison.OrdinalIgnoreCase) && hash.Length > 0 && new PasswordHasher<string>().VerifyHashedPassword(email, hash, password) != PasswordVerificationResult.Failed)
                return new DirectoryIdentity(LunchService.Email(email), config["DevelopmentIdentity:Name"] ?? "Prueba local");
            return null;
        }
        if (!config.GetValue<bool>("Ad:ContractConfirmed")) throw new BusinessException("El acceso institucional todavía requiere configurar y verificar la integración con AD.");
        if (!Uri.TryCreate(config["Ad:Endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" || string.IsNullOrWhiteSpace(config["Ad:Token"])) throw new BusinessException("La conexión institucional no está configurada.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(12)); ct = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Ad:Token"]);
        request.Content = JsonContent.Create(new Dictionary<string, string> { [config["Ad:UsernameField"] ?? "username"] = username, [config["Ad:PasswordField"] ?? "password"] = password });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return null;
        if (!response.IsSuccessStatusCode) throw new BusinessException("No se pudo validar la cuenta institucional. Intenta nuevamente.");
        if (response.Content.Headers.ContentLength > 65536) throw new BusinessException("Respuesta de identidad inválida.");
        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[65537]; var length = 0;
        while (length < buffer.Length) { var read = await responseStream.ReadAsync(buffer.AsMemory(length), ct); if (read == 0) break; length += read; }
        if (length > 65536) throw new BusinessException("Respuesta de identidad inválida.");
        var json = JsonNode.Parse(buffer.AsSpan(0, length));
        JsonNode? Field(string key, string fallback) { JsonNode? n = json; foreach (var part in (config[key] ?? fallback).Split('.')) n = n?[part]; return n; }
        if (Field("Ad:SuccessField", "success")?.GetValue<bool>() != true) return null;
        var emailValue = Field("Ad:EmailField", "user.email")?.ToString(); if (emailValue == null) throw new BusinessException("AD no devolvió un correo institucional verificado.");
        return new DirectoryIdentity(LunchService.Email(emailValue), LunchService.Bounded(Field("Ad:NameField", "user.displayName")?.ToString() ?? emailValue, 150, "Nombre"));
    }
}
