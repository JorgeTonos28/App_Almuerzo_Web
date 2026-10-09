using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Identity;

namespace Almuerzo.Web.Services;

public record DirectoryIdentity(string Email, string Name, string EmployeeCode);
public interface IDirectoryIdentityProvider { Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct); }

// ASMX HTTP POST contract from ExtraHub PR #15. Credentials never enter our database or logs.
public class DirectoryIdentityProvider(HttpClient http, IConfiguration config, IWebHostEnvironment env) : IDirectoryIdentityProvider
{
    private const int MaximumResponseBytes = 1_000_000;
    public async Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        if (env.IsDevelopment() && config.GetValue<bool>("DevelopmentIdentity:Enabled"))
        {
            var email = config["DevelopmentIdentity:Email"] ?? ""; var hash = config["DevelopmentIdentity:PasswordHash"] ?? "";
            if (username.Equals(email, StringComparison.OrdinalIgnoreCase) && hash.Length > 0 && new PasswordHasher<string>().VerifyHashedPassword(email, hash, password) != PasswordVerificationResult.Failed)
                return new DirectoryIdentity(LunchService.Email(email), config["DevelopmentIdentity:Name"] ?? "Prueba local", "0000");
            return null;
        }
        if (!config.GetValue<bool>("Ad:Enabled")) throw new BusinessException("El acceso institucional todavía no está habilitado. Contacta al administrador.");
        if (!Uri.TryCreate(config["Ad:Endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0 || endpoint.UserInfo.Length > 0 || string.IsNullOrWhiteSpace(config["Ad:Token"]))
            throw new BusinessException("La conexión institucional no está configurada.");
        var account = NormalizeAccount(username, config["Ad:DirectoryDomain"] ?? "INFOTEP");
        if (account == null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int>("Ad:TimeoutSeconds", 15), 1, 60)));
        try
        {
            var auth = await Post(endpoint, "Autenticacion", [new("Token", config["Ad:Token"]!), new("NombreUsuario", account), new("Password", password)], timeout.Token);
            var result = auth.DescendantsAndSelf().Where(x => !x.HasElements && x.Name.LocalName is "boolean" or "AutenticacionResult").Select(x => x.Value.Trim()).Distinct().ToArray();
            if (result.Length != 1 || result[0] is not ("true" or "false" or "1" or "0")) throw InvalidResponse();
            if (result[0] is "false" or "0") return null;
            var profile = await Post(endpoint, "DatosUsuarioAD", [new("Token", config["Ad:Token"]!), new("NombreUsuario", account)], timeout.Token);
            var nameColumn = ReadColumn(profile, config["Ad:FullNameColumn"] ?? "NombreCompleto", ["NombreCompleto", "Nombre", "Nombres", "FullName", "NombreEmpleado", "Empleado"]);
            var codeColumn = ReadColumn(profile, config["Ad:EmployeeCodeColumn"] ?? "Codigo", ["Codigo", "CodigoEmpleado", "CodEmpleado", "CodEmp", "EmployeeCode", "CodigoPersona", "CodPersona"]);
            if (nameColumn.Parent != codeColumn.Parent) throw InvalidResponse();
            var name = nameColumn.Value.Trim(); var code = codeColumn.Value.Trim();
            if (name.Length is 0 or > 150 || name.Any(char.IsControl) || !Regex.IsMatch(code, "^[0-9]{1,4}$", RegexOptions.CultureInvariant)) throw InvalidResponse();
            return new DirectoryIdentity(LunchService.Email(account + "@infotep.gob.do"), name, code.PadLeft(4, '0'));
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or IOException)
        { throw InvalidResponse(); }
    }

    private static string? NormalizeAccount(string username, string domain)
    {
        var account = username.Trim();
        if (account.Contains('\\'))
        {
            var parts = account.Split('\\');
            if (parts.Length != 2 || parts[1].Contains('@') || !parts[0].Equals(domain, StringComparison.OrdinalIgnoreCase)) return null;
            account = parts[1];
        }
        if (account.Contains('@'))
        {
            var parts = account.Split('@');
            if (parts.Length != 2 || !parts[1].Equals("infotep.gob.do", StringComparison.OrdinalIgnoreCase)) return null;
            account = parts[0];
        }
        return Regex.IsMatch(account, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant) ? account.ToLowerInvariant() : null;
    }

    private async Task<XElement> Post(Uri endpoint, string operation, KeyValuePair<string, string>[] fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.AbsoluteUri.TrimEnd('/') + "/" + operation) { Content = new FormUrlEncodedContent(fields) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new BusinessException("El servicio de identidad no está disponible. Intenta nuevamente.");
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw InvalidResponse();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes) throw InvalidResponse();
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        using var reader = XmlReader.Create(buffer, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumResponseBytes });
        return XElement.Load(reader);
    }

    private static XElement ReadColumn(XElement document, string configured, string[] aliases)
    {
        static string Normalize(string value) => new(XmlConvert.DecodeName(value).Normalize(NormalizationForm.FormD).Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(x)).Select(char.ToUpperInvariant).ToArray());
        foreach (var column in aliases.Prepend(configured).Select(Normalize).Distinct())
        {
            var values = document.DescendantsAndSelf().Where(x => !x.HasElements && Normalize(x.Name.LocalName) == column && x.Value.Trim().Length > 0).ToArray();
            if (values.Length > 1) throw InvalidResponse();
            if (values.Length == 1) return values[0];
        }
        throw InvalidResponse();
    }
    private static BusinessException InvalidResponse() => new("El directorio devolvió un perfil o una respuesta inválidos. Contacta al administrador.");
}
