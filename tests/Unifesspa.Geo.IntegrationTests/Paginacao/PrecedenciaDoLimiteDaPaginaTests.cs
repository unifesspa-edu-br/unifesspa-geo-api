namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net;
using System.Net.Http;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// A página que a fronteira HTTP monta a partir do limite, da direção e do cursor da requisição,
/// nas listagens de estados e de cidades. Observa a consulta recebida, sem banco.
/// </summary>
public sealed class PrecedenciaDoLimiteDaPaginaTests : IClassFixture<GeoOpenApiFactory>, IDisposable
{
    private readonly ConsultaDeListagemCapturada _consultas = new();
    private readonly HostDasListagens _host;

    public PrecedenciaDoLimiteDaPaginaTests(GeoOpenApiFactory origem)
    {
        _host = HostDasListagens.SemBanco(origem, new RelogioFixo(), _consultas);
    }

    public void Dispose() => _host.Dispose();

    [Theory(DisplayName = "Sem limite, direção nem cursor, a primeira página usa o limite padrão de 20, para a frente")]
    [InlineData("estados")]
    [InlineData("cidades")]
    public async Task SemParametros_UsaOLimitePadrao(string recurso)
    {
        await ConsultarAsync($"/api/{recurso}");

        _consultas.PaginaRecebida.Should().Be(new PageRequest(null, 20, PaginationDirection.Next));
    }

    [Theory(DisplayName = "O limite da consulta vale quando não há cursor, e o primeiro valor vence se vier repetido")]
    [InlineData("estados", "?limit=5", 5)]
    [InlineData("cidades", "?limit=5", 5)]
    [InlineData("estados", "?limit=3&limit=9", 3)]
    public async Task Limite_DaConsulta_Vale(string recurso, string consulta, int limite)
    {
        await ConsultarAsync($"/api/{recurso}{consulta}");

        _consultas.PaginaRecebida!.Limit.Should().Be(limite);
    }

    [Theory(DisplayName = "Limite e direção em branco são tratados como ausentes")]
    [InlineData("?limit=")]
    [InlineData("?limit=%20")]
    [InlineData("?direction=")]
    [InlineData("?cursor=%20")]
    public async Task Parametros_EmBranco_ViramPadrao(string consulta)
    {
        await ConsultarAsync($"/api/estados{consulta}");

        _consultas.PaginaRecebida.Should().Be(new PageRequest(null, 20, PaginationDirection.Next));
    }

    [Theory(DisplayName = "O cursor guarda o limite da página quando a consulta não traz limite")]
    [InlineData("estados")]
    [InlineData("cidades")]
    public async Task Cursor_GuardaOLimite(string recurso)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida(recurso, 7, PaginationDirection.Next));

        await ConsultarAsync($"/api/{recurso}?cursor={cursor}");

        _consultas.PaginaRecebida.Should().Be(new PageRequest(HostDasListagens.IdDaAncora, 7, PaginationDirection.Next, "maraba"));
    }

    [Theory(DisplayName = "O limite da consulta vence o limite guardado no cursor")]
    [InlineData("estados")]
    [InlineData("cidades")]
    public async Task Limite_DaConsulta_VenceODoCursor(string recurso)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida(recurso, 7, PaginationDirection.Next));

        await ConsultarAsync($"/api/{recurso}?cursor={cursor}&limit=3");

        _consultas.PaginaRecebida!.Limit.Should().Be(3);
    }

    [Theory(DisplayName = "Limite do cursor fora do intervalo é ajustado para a borda mais próxima")]
    [InlineData(500, 100)]
    [InlineData(101, 100)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task Limite_DoCursorForaDoIntervalo_EAjustado(int limiteDoCursor, int limiteEsperado)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida("estados", limiteDoCursor, PaginationDirection.Next));

        await ConsultarAsync($"/api/estados?cursor={cursor}");

        _consultas.PaginaRecebida!.Limit.Should().Be(limiteEsperado);
    }

    [Theory(DisplayName = "Direção prev sem cursor é a primeira página, para a frente")]
    [InlineData("?direction=prev")]
    [InlineData("?direction=PREV&limit=4")]
    public async Task DirecaoPrev_SemCursor_EPrimeiraPagina(string consulta)
    {
        await ConsultarAsync($"/api/estados{consulta}");

        PageRequest pagina = _consultas.PaginaRecebida!;
        (pagina.AfterId, pagina.AfterSortKey, pagina.Direction).Should().Be((null, null, PaginationDirection.Next));
    }

    [Theory(DisplayName = "Cursor da direção prev, com direction=prev em qualquer caixa, recua a partir da âncora")]
    [InlineData("&direction=prev")]
    [InlineData("&direction=PREV")]
    [InlineData("&direction=prev&direction=next")]
    public async Task Cursor_Prev_ComDirecaoPrev_Recua(string consulta)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida("estados", 7, PaginationDirection.Prev));

        await ConsultarAsync($"/api/estados?cursor={cursor}{consulta}");

        _consultas.PaginaRecebida.Should().Be(new PageRequest(HostDasListagens.IdDaAncora, 7, PaginationDirection.Prev, "maraba"));
    }

    [Fact(DisplayName = "Cursor da direção next sem direction na consulta avança a partir da âncora")]
    public async Task Cursor_Next_SemDirecao_Avanca()
    {
        string cursor = await _host.CursorAsync(_host.CargaValida("estados", 7, PaginationDirection.Next));

        await ConsultarAsync($"/api/estados?cursor={cursor}&direction=NEXT");

        _consultas.PaginaRecebida!.Direction.Should().Be(PaginationDirection.Next);
        _consultas.PaginaRecebida.AfterId.Should().Be(HostDasListagens.IdDaAncora);
    }

    private async Task ConsultarAsync(string rota)
    {
        using HttpClient cliente = _host.CriarCliente();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
