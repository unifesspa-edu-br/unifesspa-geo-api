namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Api;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// A carga das cidades grava o nome oficial do IBGE, por código, no lugar do nome que a
/// DNE entrega (sobre Postgres+PostGIS real). Código fora da lista oficial mantém o nome
/// da DNE e fica registrado no relatório.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class EtlNomeOficialDoMunicipioTests
{
    private const string CodigoForaDaLista = "1599999";

    private readonly GeoPostgisFixture _fixture;

    public EtlNomeOficialDoMunicipioTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "DNE entrega «Pau DArco»: o banco grava «Pau D'Arco» no Pará e no Tocantins, em registros distintos")]
    public async Task PauDArco_GravaNomeOficialNosDoisEstados()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await ExecutarAsync(FonteComMunicipiosDeApostrofo.Criar());

        Cidade para = await ObterAsync(FonteComMunicipiosDeApostrofo.PauDArcoPara);
        Cidade tocantins = await ObterAsync(FonteComMunicipiosDeApostrofo.PauDArcoTocantins);

        para.Nome.Should().Be("Pau D'Arco");
        para.Uf.Should().Be("PA");
        tocantins.Nome.Should().Be("Pau D'Arco");
        tocantins.Uf.Should().Be("TO");
        para.Id.Should().NotBe(tocantins.Id);
    }

    [Theory(DisplayName = "Nome oficial do IBGE e nome normalizado (sem acento, caixa nem apóstrofo) gravados por código")]
    [InlineData(FonteComMunicipiosDeApostrofo.SantaBarbaraDOeste, "Santa Bárbara d'Oeste", "santa barbara doeste")]
    [InlineData(FonteComMunicipiosDeApostrofo.SantAnaDoLivramento, "Sant'Ana do Livramento", "santana do livramento")]
    [InlineData(FonteComMunicipiosDeApostrofo.EldoradoDoCarajas, "Eldorado do Carajás", "eldorado do carajas")]
    [InlineData(FonteComMunicipiosDeApostrofo.PauDArcoPara, "Pau D'Arco", "pau darco")]
    [InlineData(FonteComMunicipiosDeApostrofo.Maraba, "Marabá", "maraba")]
    public async Task Carga_GravaNomeOficialENomeNormalizado(string codigo, string nomeOficial, string nomeNormalizado)
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await ExecutarAsync(FonteComMunicipiosDeApostrofo.Criar());

        Cidade cidade = await ObterAsync(codigo);

        cidade.Nome.Should().Be(nomeOficial);
        cidade.NomeNormalizado.Should().Be(nomeNormalizado);
    }

    [Fact(DisplayName = "Recarga da mesma versão mantém o nome oficial e o Guid de cada cidade")]
    public async Task Recarga_MantemNomeOficialEGuid()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await ExecutarAsync(FonteComMunicipiosDeApostrofo.Criar());
        List<(Guid Id, string Nome)> primeira = await CidadesAsync();

        RelatorioImportacao segunda = await ExecutarAsync(FonteComMunicipiosDeApostrofo.Criar());
        List<(Guid Id, string Nome)> depois = await CidadesAsync();

        depois.Should().BeEquivalentTo(primeira);
        depois.Select(c => c.Nome).Should().Contain("Pau D'Arco");
        segunda.Tabelas["cidade"].Inseridos.Should().Be(0);
        segunda.Tabelas["cidade"].Atualizados.Should().Be(7);
    }

    [Fact(DisplayName = "Cidade já gravada com o nome da DNE passa a ter o nome oficial na recarga, com o mesmo Guid")]
    public async Task Recarga_CorrigeNomeGravadoComODaDne()
    {
        Guid idAntes = await SemearComNomeDaDneAsync();

        await ExecutarAsync(FonteComMunicipiosDeApostrofo.Criar());

        Cidade depois = await ObterAsync(FonteComMunicipiosDeApostrofo.PauDArcoPara);
        depois.Id.Should().Be(idAntes);
        depois.Nome.Should().Be("Pau D'Arco");
        depois.NomeNormalizado.Should().Be("pau darco");
    }

    [Fact(DisplayName = "Código fora da lista oficial mantém o nome da DNE, não aborta a carga e vira amostra no relatório")]
    public async Task CodigoForaDaLista_MantemNomeDaDneERegistraAmostra()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        FonteEmMemoria fonte = FonteComMunicipiosDeApostrofo.Criar();
        fonte.Cidades.Add(DadosDne.Cidade(CodigoForaDaLista, "Cidade Fora da Lista", "PA"));

        RelatorioImportacao relatorio = await ExecutarAsync(fonte);

        Cidade cidade = await ObterAsync(CodigoForaDaLista);
        cidade.Nome.Should().Be("Cidade Fora da Lista");
        cidade.NomeNormalizado.Should().Be("cidade fora da lista");
        relatorio.Tabelas["cidade"].ParsesDegradados.Should().Be(1);
        relatorio.Tabelas["cidade"].Amostras.Should().ContainSingle(a => a == $"nome_oficial_ibge_ausente:{CodigoForaDaLista}");
        relatorio.Tabelas["cidade"].Inseridos.Should().Be(8);
    }

    private async Task<Guid> SemearComNomeDaDneAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado para = GeoReferenceSeed.NovoEstado(brasil.Id, "PA", "Pará");
        Cidade antiga = GeoReferenceSeed.NovaCidade(
            para.Id, "PA", FonteComMunicipiosDeApostrofo.PauDArcoPara, "Pau DArco", nomeNormalizado: "pau darco");
        ctx.Paises.Add(brasil);
        ctx.Estados.Add(para);
        ctx.Cidades.Add(antiga);
        await ctx.SaveChangesAsync();
        return antiga.Id;
    }

    private async Task<Cidade> ObterAsync(string codigoIbge)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return await leitura.Cidades.SingleAsync(c => c.CodigoIbge == codigoIbge);
    }

    private async Task<List<(Guid Id, string Nome)>> CidadesAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Cidade> cidades = await leitura.Cidades.OrderBy(c => c.CodigoIbge).ToListAsync();
        return [.. cidades.Select(c => (c.Id, c.Nome))];
    }

    private Task<RelatorioImportacao> ExecutarAsync(IGeoFonteDados fonte) =>
        CargaPelaEtl.ExecutarAsync(_fixture, fonte);
}
