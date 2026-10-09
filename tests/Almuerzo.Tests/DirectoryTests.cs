using System.Net;
using Almuerzo.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Almuerzo.Tests;

public class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Almuerzo.Web";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
public class DirectoryTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => callback(request, ct);
    }
    private static DirectoryIdentityProvider Provider(HttpMessageHandler handler, Dictionary<string, string?>? extras = null)
    {
        var values = new Dictionary<string, string?> { ["Ad:Enabled"] = "true", ["Ad:Endpoint"] = "https://directory.test/ADService.asmx", ["Ad:Token"] = "synthetic-token" };
        foreach (var item in extras ?? []) values[item.Key] = item.Value;
        return new(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new TestEnvironment());
    }
    [Theory]
    [InlineData("Colaborador")]
    [InlineData("COLABORADOR@INFOTEP.GOB.DO")]
    [InlineData("INFOTEP\\colaborador")]
    public async Task UsesVerifiedAsmxContractAndNormalizesInstitutionalIdentity(string username)
    {
        var operations = new List<string>();
        var provider = Provider(new Handler(async (request, ct) => {
            Assert.Equal("https", request.RequestUri!.Scheme); Assert.Null(request.Headers.Authorization);
            var fields = (await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => WebUtility.UrlDecode(x[0]), x => WebUtility.UrlDecode(x[1]));
            Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal("synthetic-token", fields["Token"]); Assert.Equal("colaborador", fields["NombreUsuario"]);
            var operation = request.RequestUri.Segments.Last(); operations.Add(operation);
            if (operation == "Autenticacion") { Assert.Equal("test&only+password", fields["Password"]); return new(HttpStatusCode.OK) { Content = new StringContent("<boolean xmlns='http://tempuri.org/'>true</boolean>") }; }
            Assert.False(fields.ContainsKey("Password"));
            return new(HttpStatusCode.OK) { Content = new StringContent("<DataSet><diffgram><NewDataSet><Table><Nombre>Colaborador</Nombre><NombreCompleto>Colaborador de Prueba</NombreCompleto><C_x00F3_digo>7</C_x00F3_digo></Table></NewDataSet></diffgram></DataSet>") };
        }));
        var identity = await provider.AuthenticateAsync(username, "test&only+password", default);
        Assert.Equal(new DirectoryIdentity("colaborador@infotep.gob.do", "Colaborador de Prueba", "0007"), identity);
        Assert.Equal(new[] { "Autenticacion", "DatosUsuarioAD" }, operations);
    }
    [Fact] public async Task RejectedCredentialsDoNotQueryProfile()
    {
        int calls = 0;
        var provider = Provider(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<boolean>false</boolean>") }); }));
        Assert.Null(await provider.AuthenticateAsync("colaborador", "invalid", default)); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("colaborador@external.test")]
    [InlineData("OTHER\\colaborador")]
    [InlineData("INFOTEP\\colaborador@infotep.gob.do")]
    [InlineData("<colaborador>")]
    public async Task InvalidAccountNamesNeverSendCredentials(string username)
    {
        int calls = 0; var provider = Provider(new Handler((_, _) => { calls++; throw new Exception("No network call expected"); }));
        Assert.Null(await provider.AuthenticateAsync(username, "test-only", default)); Assert.Equal(0, calls);
    }
    [Theory]
    [InlineData("<!DOCTYPE boolean [<!ENTITY secret SYSTEM 'file:///private'>]><boolean>&secret;</boolean>")]
    [InlineData("<boolean>unexpected-response-with-sensitive-content</boolean>")]
    [InlineData("<root><boolean>true</boolean><boolean>false</boolean></root>")]
    public async Task InvalidXmlCannotAuthenticateOrLeakProviderContent(string response)
    {
        var provider = Provider(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) })));
        var error = await Assert.ThrowsAsync<BusinessException>(() => provider.AuthenticateAsync("colaborador", "test-only", default));
        Assert.DoesNotContain("sensitive-content", error.Message); Assert.DoesNotContain("file:///", error.Message);
    }
    [Theory]
    [InlineData("<Table><NombreCompleto>Prueba</NombreCompleto></Table>")]
    [InlineData("<Table><NombreCompleto>Prueba</NombreCompleto><Codigo>ABCDE</Codigo></Table>")]
    [InlineData("<DataSet><Table><NombreCompleto>Prueba</NombreCompleto></Table><Table><Codigo>1000</Codigo></Table></DataSet>")]
    [InlineData("<DataSet><Table><NombreCompleto>Prueba</NombreCompleto><Codigo>1000</Codigo></Table><Table><NombreCompleto>Otra</NombreCompleto><Codigo>1001</Codigo></Table></DataSet>")]
    public async Task MissingOrAmbiguousProfileCannotCreateAnIdentity(string profile)
    {
        var provider = Provider(new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Segments.Last() == "Autenticacion" ? "<boolean>true</boolean>" : profile) })));
        await Assert.ThrowsAsync<BusinessException>(() => provider.AuthenticateAsync("colaborador", "test-only", default));
    }
    [Fact] public async Task ResponseLimitsAndTimeoutApplyBeforeParsing()
    {
        var provider = Provider(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1_000_001)) })));
        await Assert.ThrowsAsync<BusinessException>(() => provider.AuthenticateAsync("colaborador", "test-only", default));
        var slow = Provider(new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); }), new() { ["Ad:TimeoutSeconds"] = "1" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.AuthenticateAsync("colaborador", "test-only", default));
    }
}
