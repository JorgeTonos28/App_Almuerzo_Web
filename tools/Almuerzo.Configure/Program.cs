using System.Data.Common;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Run from the repository through scripts\Configure.cmd. Never pass secrets as command arguments.
const string secretId = "almuerzo-web-development";
const string endpoint = "https://ona-dis-2.infotep.gov.do/InfotepADWebservice/ADService.asmx";
const string developmentHttpEndpoint = "http://ona-dis-2.infotep.gov.do/InfotepADWebservice/ADService.asmx";
Console.OutputEncoding = Encoding.UTF8;
try
{
    var mode = args.FirstOrDefault() ?? "help";
    var values = new Dictionary<string, string>();
    switch (mode)
    {
        case "ad":
            Console.WriteLine("Actualiza AD local. Conserva la conexión SQL y los demás secretos. Mapea primero los catálogos importados (modo map).");
            values["Ad:Token"] = Secret("Token AD nuevo");
            values["Ad:Endpoint"] = endpoint; values["Ad:Enabled"] = "true";
            values["Ad:AllowInsecureHttpInDevelopment"] = "false";
            values["Ad:OrganizationContract"] = "NamesV1"; values["Ad:EmployeeCodeColumn"] = "CodigoEmpleado";
            values["Ad:AutoProvisioningEnabled"] = "true"; values["Ad:AutoCreateOrganizationEnabled"] = "true";
            values["DevelopmentIdentity:Enabled"] = "false";
            await SaveSecrets(values);
            break;
        case "ad-http-test":
            Console.WriteLine("AD HTTP de pruebas: solo Development, solo el host institucional y solo con un token/cuenta de prueba. Las credenciales viajarán sin cifrar; revoca el token al terminar.");
            if (Prompt("Escribe ENABLE_HTTP_TEST para continuar") != "ENABLE_HTTP_TEST") throw new InvalidOperationException();
            values["Ad:Token"] = Secret("Token AD de pruebas");
            values["Ad:Endpoint"] = developmentHttpEndpoint; values["Ad:Enabled"] = "true";
            values["Ad:AllowInsecureHttpInDevelopment"] = "true";
            values["Ad:OrganizationContract"] = "NamesV1"; values["Ad:EmployeeCodeColumn"] = "CodigoEmpleado";
            values["Ad:AutoProvisioningEnabled"] = "true"; values["Ad:AutoCreateOrganizationEnabled"] = "true";
            values["DevelopmentIdentity:Enabled"] = "false";
            await SaveSecrets(values);
            break;
        case "map":
            Console.WriteLine("Vincula IDs del catálogo privado con nombres exactos de AD. No conecta a SQL ni modifica históricos.");
            var secretsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", secretId, "secrets.json");
            var current = File.Exists(secretsPath) ? JsonNode.Parse(await File.ReadAllTextAsync(secretsPath))!.AsObject() : new JsonObject();
            var name = Prompt("Nombre de regional en AD"); var id = Prompt("ID interno de la regional existente");
            int regionIndex = 0;
            while (current[$"Ad:OrganizationMappings:{regionIndex}:Name"] is { } existing && Normalize(existing.ToString()) != Normalize(name)) regionIndex++;
            var prefix = $"Ad:OrganizationMappings:{regionIndex}:";
            if (current[prefix + "Id"] is { } previous && previous.ToString() != id)
            {
                Console.WriteLine("Ya existe un mapeo para este nombre AD. Cambiar el ID puede mover el acceso a otro catálogo y requiere reconciliación explícita.");
                if (Prompt("Escribe RECONCILE_MAPPING para reemplazarlo") != "RECONCILE_MAPPING") throw new InvalidOperationException();
            }
            values[prefix + "Name"] = name; values[prefix + "Id"] = id;
            Console.WriteLine("NamesV1 solo mapea regionales. Los departamentos AD se registran aparte; los grupos de almuerzo se asignan en el panel.");
            await SaveSecrets(values);
            break;
        case "ti":
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Este asistente de entrega privada requiere Windows.");
            Console.WriteLine("Genera private\ti\appsettings.Production.json con permisos exclusivos de tu usuario. No cambia el entorno local ni conecta al servidor.");
            var server = Prompt("Servidor SQL (TI debe confirmar instancia/puerto)", "ona-dis-2.infotep.gov.do");
            var database = Prompt("Base de datos", "AlmuerzoBD"); var username = Prompt("Usuario SQL", "almuerzo");
            var connection = new DbConnectionStringBuilder { ["Server"] = server, ["Database"] = database, ["User ID"] = username, ["Password"] = Secret("Contraseña SQL"), ["Encrypt"] = true, ["TrustServerCertificate"] = false, ["Persist Security Info"] = false };
            var host = Prompt("Hostname HTTPS definitivo de la aplicación (sin protocolo)");
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('*') || host.Contains(';')) throw new InvalidOperationException("Hostname inválido.");
            var config = new JsonObject {
                ["AllowedHosts"] = host, ["Database"] = new JsonObject { ["Provider"] = "SqlServer" },
                ["ConnectionStrings"] = new JsonObject { ["DefaultConnection"] = connection.ConnectionString },
                ["Storage"] = new JsonObject { ["Path"] = @"C:\ProgramData\Almuerzo" },
                ["Ad"] = new JsonObject { ["Enabled"] = true, ["Endpoint"] = endpoint, ["Token"] = Secret("Token AD para TI"), ["OrganizationContract"] = "NamesV1", ["EmployeeCodeColumn"] = "CodigoEmpleado", ["AutoProvisioningEnabled"] = true, ["AutoCreateOrganizationEnabled"] = true },
                ["DevelopmentIdentity"] = new JsonObject { ["Enabled"] = false },
                ["Operations"] = new JsonObject { ["Enabled"] = false, ["ScheduledJobsEnabled"] = false },
                ["Mail"] = new JsonObject { ["Mode"] = "Pickup" }
            };
            var destination = Path.GetFullPath(Path.Combine("private", "ti"));
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            var directory = new DirectoryInfo(destination); directory.Create(security); directory.SetAccessControl(security);
            var output = Path.Combine(destination, "appsettings.Production.json");
            if (File.Exists(output)) throw new InvalidOperationException("Ya existe el archivo privado. Consérvalo o muévelo antes de generar otro.");
            await File.WriteAllTextAsync(output, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Archivo privado generado. TI debe ajustar almacenamiento, importar los mapeos, asegurar permisos y configurar SendGrid antes de habilitar envíos. No incluyas este archivo en Git ni en el ZIP público.");
            break;
        default:
            Console.WriteLine("Uso: scripts\\Configure.cmd ad | ad-http-test | map | ti\nad: token y contrato AD seguro en User Secrets local.\nad-http-test: excepción temporal HTTP solo en Development para el endpoint de pruebas.\nmap: equivalencias con catálogos importados en User Secrets.\nti: configuración privada independiente para entregar a TI.");
            return 0;
    }
    Console.WriteLine("Configuración preparada. Reinicia/despliega la aplicación para aplicarla."); return 0;
}
catch (Exception)
{
    // Exception messages may contain connection details or tool output; never echo them.
    Console.Error.WriteLine("No se completó la configuración. Verifica los datos, permisos, mapeos existentes y disponibilidad de dotnet. No se muestran secretos ni respuestas de herramientas."); return 1;
}

static string Prompt(string label, string? fallback = null, bool optional = false)
{
    Console.Write(label + (fallback == null ? "" : $" [{fallback}]") + ": ");
    var value = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(value)) value = fallback ?? "";
    if (!optional && value.Length == 0 || value.Any(char.IsControl)) throw new InvalidOperationException();
    return value;
}
static string Normalize(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim().Normalize(NormalizationForm.FormC), @"\s+", " ").ToUpperInvariant();
static string Secret(string label)
{
    if (Console.IsInputRedirected) throw new InvalidOperationException("Se requiere una consola interactiva.");
    Console.Write(label + " (entrada oculta): "); var result = new StringBuilder();
    while (true) {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
        if (key.Key == ConsoleKey.Backspace) { if (result.Length > 0) result.Length--; }
        else if (!char.IsControl(key.KeyChar)) result.Append(key.KeyChar);
    }
    Console.WriteLine();
    if (string.IsNullOrWhiteSpace(result.ToString()) || result.ToString() == "***") throw new InvalidOperationException();
    return result.ToString();
}
static async Task SaveSecrets(Dictionary<string, string> values)
{
    var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "user-secrets", "set", "--id", "almuerzo-web-development" }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException();
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteAsync(JsonSerializer.Serialize(values)); process.StandardInput.Close();
    await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
    if (process.ExitCode != 0) throw new InvalidOperationException();
    Console.WriteLine("Valores guardados en User Secrets. La conexión SQL local permanece intacta.");
}
