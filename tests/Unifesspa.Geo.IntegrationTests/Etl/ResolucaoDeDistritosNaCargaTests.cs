namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDasLocalidades;

/// <summary>
/// Resolução do distrito de cada logradouro na carga das folhas, sobre Postgres+PostGIS real:
/// o distrito é único por cidade e nome, distritos homônimos de cidades distintas coexistem e
/// o distrito de outra cidade nunca é ligado ao logradouro.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ResolucaoDeDistritosNaCargaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDasLocalidades _cenario;

    public ResolucaoDeDistritosNaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDasLocalidades(fixture);
    }

    [Fact(DisplayName = "CA-06: upsert de distrito por (cidade, nome) — duplicado na mesma cidade não duplica")]
    public async Task DistritoDuplicado_NaMesmaCidade_NaoDuplica()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteCompleta();
        // Mesmo (cidade, nome) de um distrito já presente, com outro id_distrito da fonte.
        fonte.Distritos.Add(DadosDne.Distrito(11, "Cidade Nova", IdCidadeMaraba));

        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Distritos.CountAsync(d => d.Nome == "Cidade Nova")).Should().Be(1);
        relatorio.Tabelas["distrito"].Duplicados.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact(DisplayName = "CA-06 + FK: distritos homônimos em cidades distintas coexistem e o logradouro resolve para o Guid correto via id_distrito")]
    public async Task DistritoHomonimo_EmCidadesDistintas_ResolveFkCorreta()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteCompleta();
        // "Centro" existe em Marabá (id 30) e em São Paulo (id 20 já no FonteCompleta).
        fonte.Distritos.Add(DadosDne.Distrito(30, "Centro", IdCidadeMaraba));
        // Dois logradouros, um por "Centro", referenciando o id_distrito respectivo.
        fonte.Logradouros.Add(DadosDne.Logradouro("68500100", "Av Marabá", IdCidadeMaraba, distritoIdDne: 30));
        fonte.Logradouros.Add(DadosDne.Logradouro("01000000", "Av Paulista", IdCidadeSaoPaulo, distritoIdDne: 20));

        await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid marabaId = (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == CodigoMaraba)).Id;
        Guid spId = (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == CodigoSaoPaulo)).Id;

        (await leitura.Distritos.CountAsync(d => d.Nome == "Centro")).Should().Be(2);

        Distrito centroMaraba = await leitura.Distritos.SingleAsync(d => d.Nome == "Centro" && d.CidadeId == marabaId);
        Distrito centroSp = await leitura.Distritos.SingleAsync(d => d.Nome == "Centro" && d.CidadeId == spId);
        centroMaraba.Id.Should().NotBe(centroSp.Id);

        Logradouro avMaraba = await leitura.Logradouros.SingleAsync(l => l.Nome == "Av Marabá");
        Logradouro avPaulista = await leitura.Logradouros.SingleAsync(l => l.Nome == "Av Paulista");
        avMaraba.DistritoId.Should().Be(centroMaraba.Id);
        avPaulista.DistritoId.Should().Be(centroSp.Id);
    }

    [Fact(DisplayName = "Coerência: distrito_id que resolve para distrito de outra cidade degrada para null (não cruza cidade)")]
    public async Task DistritoDeOutraCidade_DegradaParaNull()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteCompleta();
        // Logradouro em Marabá apontando para o distrito 20 ("Centro"), que é de São Paulo.
        fonte.Logradouros.Add(DadosDne.Logradouro("68500200", "Rua Cruzada", IdCidadeMaraba, distritoIdDne: 20));

        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Logradouro cruzada = await leitura.Logradouros.SingleAsync(l => l.Nome == "Rua Cruzada");
        cruzada.DistritoId.Should().BeNull("o distrito resolvido é de outra cidade — não pode cruzar");
        relatorio.Tabelas["logradouro"].ParsesDegradados.Should().BeGreaterThanOrEqualTo(1);
    }
}
