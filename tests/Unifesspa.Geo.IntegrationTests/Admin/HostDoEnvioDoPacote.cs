namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Globalization;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Fixtures.Authentication;

/// <summary>Quem envia o pacote: sem login, com login sem o papel administrativo, ou administrador.</summary>
internal enum PerfilDoEnvio
{
    Anonimo,
    SemPapelAdministrativo,
    Administrador,
}

/// <summary>
/// Servidor de teste do envio do pacote, com diretório de pacotes próprio, limite de tamanho
/// configurável e registro de log em memória. Ao descartar, solta as cargas que ficaram na fila.
/// </summary>
internal sealed class HostDoEnvioDoPacote : IAsyncDisposable
{
    public const string Rota = "/api/admin/geo/importacoes";

    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly HttpClient _cliente;

    public HostDoEnvioDoPacote(
        WebApplicationFactory<Program> fabrica,
        long? limiteEmBytes = null,
        Action<IServiceCollection>? ajustarServicos = null)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        Diretorio = Path.Combine(Path.GetTempPath(), $"envio-pacote-{Guid.NewGuid():N}");
        _fabrica = fabrica.WithWebHostBuilder(builder => Configurar(builder, limiteEmBytes, ajustarServicos));
        _cliente = _fabrica.CreateClient();
    }

    public string Diretorio { get; }

    public RegistroDeLogs Logs { get; } = new();

    /// <summary>Nomes dos arquivos que restaram no diretório de pacotes.</summary>
    public IReadOnlyList<string> NomesDosArquivos() =>
        Directory.Exists(Diretorio) ? [.. Directory.EnumerateFiles(Diretorio).Select(Path.GetFileName).OfType<string>()] : [];

    public async Task<HttpResponseMessage> EnviarAsync(HttpContent corpo, PerfilDoEnvio perfil = PerfilDoEnvio.Administrador)
    {
        using HttpRequestMessage requisicao = new(HttpMethod.Post, Rota) { Content = corpo };
        Autenticar(requisicao, perfil);
        return await _cliente.SendAsync(requisicao);
    }

    public async Task<HttpResponseMessage> ObterAsync(Uri local)
    {
        using HttpRequestMessage requisicao = new(HttpMethod.Get, local);
        Autenticar(requisicao, PerfilDoEnvio.Administrador);
        return await _cliente.SendAsync(requisicao);
    }

    public static ByteArrayContent Zip(byte[] bytes)
    {
        ByteArrayContent conteudo = new(bytes);
        conteudo.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return conteudo;
    }

    public async ValueTask DisposeAsync()
    {
        IGeoImportacaoFila fila = _fabrica.Services.GetRequiredService<IGeoImportacaoFila>();
        foreach (CargaReservada carga in fila.DrenarRestante())
        {
            await carga.DisposeAsync();
        }

        _cliente.Dispose();
        await _fabrica.DisposeAsync();
        if (Directory.Exists(Diretorio))
        {
            Directory.Delete(Diretorio, recursive: true);
        }
    }

    private static void Autenticar(HttpRequestMessage requisicao, PerfilDoEnvio perfil)
    {
        if (perfil == PerfilDoEnvio.Anonimo)
        {
            return;
        }

        requisicao.Headers.Add("Authorization", $"{TestAuthHandler.AuthorizationScheme} {TestAuthHandler.TokenValue}");
        requisicao.Headers.Add(
            TestAuthHandler.RolesHeader,
            perfil == PerfilDoEnvio.Administrador ? "plataforma-admin" : "operador-geo");
    }

    private void Configurar(IWebHostBuilder builder, long? limiteEmBytes, Action<IServiceCollection>? ajustarServicos)
    {
        List<KeyValuePair<string, string?>> opcoes = [new($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.DiretorioDoPacote)}", Diretorio)];
        if (limiteEmBytes is not null)
        {
            opcoes.Add(new(
                $"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TamanhoMaximoDoPacote)}",
                limiteEmBytes.Value.ToString(CultureInfo.InvariantCulture)));
        }

        builder.ConfigureAppConfiguration((_, configuracao) => configuracao.AddInMemoryCollection(opcoes));
        builder.ConfigureTestServices(servicos =>
        {
            servicos.AddSingleton<ILoggerFactory>(new FabricaDeLogsParaTeste(Logs));
            ajustarServicos?.Invoke(servicos);
        });
    }
}
