namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDaCargaPeriodica;

/// <summary>
/// Recarga da carga DNE sobre Postgres+PostGIS real: a linha que a nova versão (ou a recarga da
/// mesma versão) deixou de trazer fica não vigente, sem ser removida; a linha revista continua
/// vigente; versão vazia ou sem logradouros é recusada e não marca nada na base existente.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class MarcacaoDeNaoVigentesNaRecargaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaCargaPeriodica _cenario;

    public MarcacaoDeNaoVigentesNaRecargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaCargaPeriodica(fixture);
    }

    [Fact(DisplayName = "CA-03/04/05: recarga atualiza por chave natural, marca linhas ausentes como vigente=false (sem remover) e sela a versão vigente do cache")]
    public async Task Recarga_AtualizaPorChaveNatural_MarcaNaoVigentes_E_SelaCache()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
            ["202602"] = FonteSomenteMaraba("202602"),
        });

        Guid id1 = await _cenario.CriarExecucaoAsync("202601");
        await _cenario.ExecutarAsync(id1, fonteFactory, cache);

        Guid id2 = await _cenario.CriarExecucaoAsync("202602");
        await _cenario.ExecutarAsync(id2, fonteFactory, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();

        Cidade maraba = await leitura.Cidades.SingleAsync(c => c.CodigoIbge == Maraba);
        maraba.VersaoDataset.Should().Be("202602", "Marabá foi revisto na nova versão");
        maraba.Vigente.Should().BeTrue();

        Cidade saoPaulo = await leitura.Cidades.SingleAsync(c => c.CodigoIbge == SaoPaulo);
        saoPaulo.VersaoDataset.Should().Be("202601", "São Paulo não consta na versão 202602");
        saoPaulo.Vigente.Should().BeFalse("linha ausente na nova versão é marcada obsoleta");
        (await leitura.Cidades.CountAsync()).Should().Be(2, "marcar como não vigente não remove fisicamente (rastreabilidade)");

        Logradouro avPaulista = await leitura.Logradouros.SingleAsync(l => l.Cep == CepSaoPaulo);
        avPaulista.Vigente.Should().BeFalse("o CEP ausente na nova versão fica obsoleto, sem remoção");

        Logradouro ruaA = await leitura.Logradouros.SingleAsync(l => l.Cep == CepMaraba);
        ruaA.VersaoDataset.Should().Be("202602");
        ruaA.Vigente.Should().BeTrue();

        await cache.Received(1).InvalidarAsync(Arg.Is<string>(v => v.StartsWith("202602.", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Recarga marca como não vigente o CEP geral ausente da nova versão e mantém o presente")]
    public async Task Recarga_MarcaCepGeralAusenteComoNaoVigente()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteEmMemoria primeira = FonteCompleta("202601");
        primeira.CepGerais.Add(DadosDne.CepGeral("68525000", 1));
        primeira.CepGerais.Add(DadosDne.CepGeral("01000999", 2, uf: "SP"));
        FonteEmMemoria segunda = FonteSomenteMaraba("202602");
        segunda.CepGerais.Add(DadosDne.CepGeral("68525000", 1));
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = primeira,
            ["202602"] = segunda,
        });

        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), fonteFactory, cache);
        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202602"), fonteFactory, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.CepGerais.SingleAsync(g => g.Cep == "68525000")).Vigente.Should().BeTrue();
        (await leitura.CepGerais.SingleAsync(g => g.Cep == "01000999")).Vigente.Should().BeFalse();
    }

    [Fact(DisplayName = "Recarga da MESMA versão sem uma linha a marca como não vigente (vigente=false); a linha revista continua vigente")]
    public async Task RecargaMesmaVersao_MarcaLinhaAusenteComoNaoVigente()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteEmMemoria completa = FonteCompleta("202601");
        completa.CepGerais.Add(DadosDne.CepGeral("68525000", 1));
        completa.CepGerais.Add(DadosDne.CepGeral("01000999", 2, uf: "SP"));
        FonteEmMemoria semSaoPaulo = FonteSomenteMaraba("202601");
        semSaoPaulo.CepGerais.Add(DadosDne.CepGeral("68525000", 1));
        FonteFactoryFake primeira = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal) { ["202601"] = completa });
        FonteFactoryFake segunda = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal) { ["202601"] = semSaoPaulo });

        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), primeira, cache);
        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), segunda, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.CepGerais.SingleAsync(g => g.Cep == "01000999")).Vigente.Should().BeFalse();
        (await leitura.CepGerais.SingleAsync(g => g.Cep == "68525000")).Vigente.Should().BeTrue();
        (await leitura.Logradouros.SingleAsync(l => l.Cep == CepSaoPaulo)).Vigente.Should().BeFalse();
        (await leitura.Logradouros.SingleAsync(l => l.Cep == CepMaraba)).Vigente.Should().BeTrue();
        (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == SaoPaulo)).Vigente.Should().BeFalse();
        (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == Maraba)).Vigente.Should().BeTrue();
    }

    [Fact(DisplayName = "Recarga idêntica da MESMA versão não marca nenhuma linha como não vigente em nenhuma tabela")]
    public async Task RecargaIdenticaMesmaVersao_NaoMarcaNenhumaLinha()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();
        FonteEmMemoria fonte = FonteCompleta("202601");
        fonte.CepGerais.Add(DadosDne.CepGeral("68525000", 1));
        fonte.CepGerais.Add(DadosDne.CepGeral("68525100", 1, distritoIdDne: 10));
        fonte.CidadeIndicadores.Add(DadosDne.CidadeIndicador(Maraba, populacao: "100"));
        FonteFactoryFake fabrica = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal) { ["202601"] = fonte });

        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), fabrica, cache);
        await _cenario.ExecutarAsync(await _cenario.CriarExecucaoAsync("202601"), fabrica, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        int naoVigentes =
            await leitura.Paises.CountAsync(e => !e.Vigente)
            + await leitura.Estados.CountAsync(e => !e.Vigente)
            + await leitura.EstadoIndicadores.CountAsync(e => !e.Vigente)
            + await leitura.Cidades.CountAsync(e => !e.Vigente)
            + await leitura.CidadeIndicadores.CountAsync(e => !e.Vigente)
            + await leitura.Distritos.CountAsync(e => !e.Vigente)
            + await leitura.Logradouros.CountAsync(e => !e.Vigente)
            + await leitura.CepGerais.CountAsync(e => !e.Vigente);
        naoVigentes.Should().Be(0, "toda linha foi revista na recarga idêntica");
        (await leitura.CepGerais.CountAsync()).Should().Be(2);
    }

    [Fact(DisplayName = "Integridade: recarga com versão vazia ou sem o Brasil e suas cidades falha e NÃO marca a base existente como obsoleta")]
    public async Task RecargaVazia_Falha_SemMarcarNaoVigentes()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();

        // Staging corrompido: topo sem cidades, mas com uma folha (logradouro) referenciando
        // uma cidade existente. A guarda deve abortar ANTES das folhas, então "Rua Nova" não
        // pode ser commitada.
        FonteEmMemoria recargaCorrompida = new() { Versao = "202602" };
        recargaCorrompida.CidadeIds.Add(DadosDne.CidadeId(1, Maraba));
        recargaCorrompida.Logradouros.Add(DadosDne.Logradouro("68500999", "Rua Nova", 1));

        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
            ["202602"] = recargaCorrompida,
        });

        Guid id1 = await _cenario.CriarExecucaoAsync("202601");
        await _cenario.ExecutarAsync(id1, fonteFactory, cache);

        Guid id2 = await _cenario.CriarExecucaoAsync("202602");
        await _cenario.ExecutarAsync(id2, fonteFactory, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao exec2 = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id2);
        exec2.Status.Should().Be(StatusImportacao.Falhou, "uma versão vazia ou sem o Brasil e suas cidades não pode concluir");

        Cidade maraba = await leitura.Cidades.SingleAsync(c => c.CodigoIbge == Maraba);
        maraba.Vigente.Should().BeTrue("a versão vazia não pode marcar a base existente como obsoleta");
        maraba.VersaoDataset.Should().Be("202601");
        (await leitura.Logradouros.AnyAsync(l => l.Nome == "Rua Nova")).Should()
            .BeFalse("as folhas não podem rodar quando a topo não trouxe cidades");
        await cache.DidNotReceive().InvalidarAsync("202602", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Integridade: recarga com cidades mas sem logradouros (dump-folha vazio) falha e NÃO marca o CEP existente como obsoleto")]
    public async Task RecargaSemLogradouros_Falha_PreservaLookup()
    {
        await _cenario.LimparAsync();
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();

        // Topo restaurado (cidades), mas dump das tabelas-folha vazio (sem logradouros).
        FonteEmMemoria folhasVazias = new() { Versao = "202602" };
        folhasVazias.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        folhasVazias.Estados.Add(DadosDne.Estado("PA", "Pará", capital: "Belém", faixaIni: "66000000", faixaFim: "68899999"));
        folhasVazias.EstadoIndicadores.Add(DadosDne.EstadoIndicador("PA", "15"));
        folhasVazias.Cidades.Add(DadosDne.Cidade(Maraba, "Marabá", "PA", ddd: "94"));
        folhasVazias.CidadeIds.Add(DadosDne.CidadeId(1, Maraba));

        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
            ["202602"] = folhasVazias,
        });

        Guid id1 = await _cenario.CriarExecucaoAsync("202601");
        await _cenario.ExecutarAsync(id1, fonteFactory, cache);

        Guid id2 = await _cenario.CriarExecucaoAsync("202602");
        await _cenario.ExecutarAsync(id2, fonteFactory, cache);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao exec2 = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id2);
        exec2.Status.Should().Be(StatusImportacao.Falhou, "uma versão sem logradouros não pode concluir");

        Logradouro ruaA = await leitura.Logradouros.SingleAsync(l => l.Cep == CepMaraba);
        ruaA.Vigente.Should().BeTrue("a versão sem logradouros não pode marcar o CEP existente como obsoleto");
        ruaA.VersaoDataset.Should().Be("202601");
        await cache.DidNotReceive().InvalidarAsync("202602", Arg.Any<CancellationToken>());
    }
}
