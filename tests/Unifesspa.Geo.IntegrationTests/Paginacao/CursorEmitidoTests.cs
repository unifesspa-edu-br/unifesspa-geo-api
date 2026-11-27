namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Api;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// O conteúdo dos cursores que a API emite nos links <c>prev</c> e <c>next</c>, lido com o
/// mesmo codificador do servidor, contra a API real e o PostGIS.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CursorEmitidoTests : IDisposable
{
    private static readonly string[] Ufs = ["AC", "AL", "AM", "BA", "CE"];

    private readonly GeoPostgisFixture _fixture;
    private readonly RelogioFixo _relogio = new();
    private readonly HostDasListagens _host;

    public CursorEmitidoTests(GeoPostgisFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
        _host = new HostDasListagens(fixture.Factory, _relogio);
    }

    public void Dispose() => _host.Dispose();

    [Fact(DisplayName = "O cursor do next guarda o recurso, a direção, o limite e a âncora do último item, sem usuário")]
    public async Task Next_GuardaRecursoDirecaoLimiteEAncora()
    {
        await new CenarioDeEstados(_fixture).SemearEstadosAsync(Ufs);
        (Guid id, string chave) = await ChaveDoEstadoAsync("AL");

        CursorPayload carga = await CursorDoLinkAsync("/api/estados?limit=2", "next");

        carga.Should().Be(new CursorPayload(
            id.ToString(), 2, "estados", _relogio.GetUtcNow() + _host.Opcoes.CursorTtl, PaginationDirection.Next, UserId: null, SortKey: chave));
    }

    [Fact(DisplayName = "O cursor do prev guarda a direção prev, o limite da página e a âncora do primeiro item")]
    public async Task Prev_GuardaDirecaoLimiteEAncoraDoPrimeiroItem()
    {
        await new CenarioDeEstados(_fixture).SemearEstadosAsync(Ufs);
        (Guid id, string chave) = await ChaveDoEstadoAsync("AM");
        string segundaPagina = await EnderecoDoLinkAsync("/api/estados?limit=2", "next");

        CursorPayload carga = await CursorDoLinkAsync(segundaPagina, "prev");

        (carga.Direction, carga.Limit, carga.After, carga.SortKey).Should().Be((PaginationDirection.Prev, 2, id.ToString(), chave));
    }

    [Fact(DisplayName = "Os cursores do prev e do next vencem no relógio mais o prazo de validade, e no mesmo instante")]
    public async Task Cursores_VencemNoRelogioMaisOPrazo()
    {
        await new CenarioDeEstados(_fixture).SemearEstadosAsync(Ufs);
        string segundaPagina = await EnderecoDoLinkAsync("/api/estados?limit=2", "next");
        DateTimeOffset esperado = _relogio.GetUtcNow() + _host.Opcoes.CursorTtl;

        CursorPayload anterior = await CursorDoLinkAsync(segundaPagina, "prev");
        CursorPayload seguinte = await CursorDoLinkAsync(segundaPagina, "next");

        (anterior.ExpiresAt, seguinte.ExpiresAt).Should().Be((esperado, esperado));
    }

    [Fact(DisplayName = "Mesmo com o relógio avançando a cada leitura, os cursores do prev e do next vencem no mesmo instante")]
    public async Task Cursores_VencemNoMesmoInstante_ComORelogioAvancando()
    {
        await new CenarioDeEstados(_fixture).SemearEstadosAsync(Ufs);
        RelogioQueAvancaACadaLeitura relogio = new(new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero));
        using HostDasListagens host = new(_fixture.Factory, relogio);
        using HttpClient cliente = host.CriarCliente();
        string segundaPagina = await EnderecoDoLinkAsync(cliente, "/api/estados?limit=2", "next");

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, segundaPagina);
        CursorPayload anterior = await CursorDoLinkAsync(host, resposta, "prev");
        CursorPayload seguinte = await CursorDoLinkAsync(host, resposta, "next");

        seguinte.ExpiresAt.Should().Be(anterior.ExpiresAt).And.BeAfter(relogio.Inicio + host.Opcoes.CursorTtl);
    }

    [Fact(DisplayName = "O cursor das cidades guarda o recurso cidades e a chave de ordenação do último item")]
    public async Task Next_DasCidades_GuardaAChaveDeOrdenacao()
    {
        await new CenarioDeCidades(_fixture).SemearAsync();
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Cidade maraba = await ctx.Cidades.AsNoTracking().SingleAsync(cidade => cidade.CodigoIbge == "1500402");

        CursorPayload carga = await CursorDoLinkAsync("/api/cidades?uf=PA&limit=1", "next");

        (carga.ResourceTag, carga.After, carga.SortKey).Should().Be(("cidades", maraba.Id.ToString(), maraba.NomeOrdenacao));
    }

    [Fact(DisplayName = "O cursor dos distritos, paginados por identificador, não leva chave de ordenação nem usuário")]
    public async Task Next_DosDistritos_NaoLevaChaveDeOrdenacao()
    {
        await new CenarioDeDistritosPaginados(_fixture).SemearAsync(3);
        using HttpClient cliente = _host.CriarCliente();
        PaginaDeDistritos pagina = await PaginaDeDistritos.LerAsync(cliente, "/api/cidades/1500402/distritos?limit=2");

        CursorPayload carga = await CursorDoLinkAsync("/api/cidades/1500402/distritos?limit=2", "next");

        (carga.ResourceTag, carga.After, carga.SortKey, carga.UserId).Should().Be(("distritos", pagina.Ids[^1].ToString(), null, null));
    }

    private async Task<(Guid Id, string Chave)> ChaveDoEstadoAsync(string uf)
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Estado estado = await ctx.Estados.AsNoTracking().SingleAsync(e => e.Uf == uf);
        return (estado.Id, estado.NomeOrdenacao);
    }

    private async Task<string> EnderecoDoLinkAsync(string rota, string rel)
    {
        using HttpClient cliente = _host.CriarCliente();
        return await EnderecoDoLinkAsync(cliente, rota, rel);
    }

    private static async Task<string> EnderecoDoLinkAsync(HttpClient cliente, string rota, string rel)
    {
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, rota);
        return CabecalhoLinkDaResposta.Endereco(resposta, rel)!.Texto;
    }

    private async Task<CursorPayload> CursorDoLinkAsync(string rota, string rel)
    {
        using HttpClient cliente = _host.CriarCliente();
        return await CursorDoLinkAsync(_host, cliente, rota, rel);
    }

    private static async Task<CursorPayload> CursorDoLinkAsync(HostDasListagens host, HttpClient cliente, string rota, string rel)
    {
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, rota);
        return await CursorDoLinkAsync(host, resposta, rel);
    }

    private static async Task<CursorPayload> CursorDoLinkAsync(HostDasListagens host, HttpResponseMessage resposta, string rel)
    {
        string cursor = CabecalhoLinkDaResposta.Endereco(resposta, rel)!.Valor("cursor")!;
        return (await host.DecodificarAsync(cursor)).Payload!;
    }
}
