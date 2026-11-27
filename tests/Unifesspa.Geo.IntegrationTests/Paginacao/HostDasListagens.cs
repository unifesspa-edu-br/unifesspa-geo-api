namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net.Http;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.IntegrationTests.Fixtures.Authentication;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Servidor da API com o relógio do teste, para as listagens paginadas. Também fabrica e lê cursores
/// com o mesmo codificador do servidor.
/// </summary>
internal sealed class HostDasListagens : IDisposable
{
    public static readonly Guid IdDaAncora = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly TimeProvider _relogio;

    public HostDasListagens(
        WebApplicationFactory<Program> origem,
        TimeProvider relogio,
        Action<IServiceCollection>? ajustarServicos = null)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(relogio);

        _relogio = relogio;
        _fabrica = origem.WithWebHostBuilder(builder => builder.ConfigureTestServices(servicos =>
        {
            servicos.AddSingleton(relogio);
            ajustarServicos?.Invoke(servicos);
        }));
    }

    public IServiceProvider Servicos => _fabrica.Services;

    public CursorPaginationOptions Opcoes => Servicos.GetRequiredService<IOptions<CursorPaginationOptions>>().Value;

    /// <summary>Servidor sem banco: as consultas de listagem são respondidas pela captura informada.</summary>
    public static HostDasListagens SemBanco(
        GeoOpenApiFactory origem,
        TimeProvider relogio,
        ConsultaDeListagemCapturada consultas) =>
        new(origem, relogio, servicos =>
        {
            servicos.RemoveAll<IQueryBus>();
            servicos.AddSingleton<IQueryBus>(consultas);
        });

    public HttpClient CriarCliente()
    {
        HttpClient cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.AuthorizationScheme, TestAuthHandler.TokenValue);
        return cliente;
    }

    /// <summary>Faz o servidor tratar o prefixo como o início de todos os caminhos, como atrás de um proxy.</summary>
    public void FixarPrefixoDoCaminho(string prefixo) =>
        _fabrica.Server.BaseAddress = new Uri($"http://localhost{prefixo}/");

    public CursorPayload CargaValida(string recurso, int limite, PaginationDirection direcao) =>
        new(IdDaAncora.ToString(), limite, recurso, _relogio.GetUtcNow() + Opcoes.CursorTtl, direcao, UserId: null, SortKey: "maraba");

    public Task<string> CursorAsync(CursorPayload carga) =>
        Servicos.GetRequiredService<CursorEncoder>().EncodeAsync(carga);

    public Task<CursorDecodeResult> DecodificarAsync(string cursor) =>
        Servicos.GetRequiredService<CursorEncoder>().TryDecodeAsync(cursor);

    public void Dispose() => _fabrica.Dispose();
}
