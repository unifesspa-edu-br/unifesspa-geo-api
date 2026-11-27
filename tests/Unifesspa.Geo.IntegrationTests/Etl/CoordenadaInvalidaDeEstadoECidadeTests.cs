namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Coordenada de estado e de cidade que não cabe no domínio geográfico, ou que não cabe na coluna
/// numérica, vira "sem coordenada" (latitude, longitude e ponto vazios) e não derruba a carga,
/// como já acontece com distrito, bairro, CEP geral e logradouro. Coordenada válida não muda.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CoordenadaInvalidaDeEstadoECidadeTests
{
    private const string Uf = "PA";
    private const string CodigoDeMaraba = VersoesFicticias.CodigoMaraba;

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public CoordenadaInvalidaDeEstadoECidadeTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Estado com latitude fora do domínio: a carga conclui e o estado fica sem latitude, sem longitude e sem ponto")]
    public async Task Estado_LatitudeForaDoDominio_FicaSemCoordenada()
    {
        await PublicarConcluindoAsync(FonteComEstadoNaPosicao("95.5", "-50.5"));

        (await PosicaoDoEstadoAsync()).Should().Be(PosicaoGravada.Ausente);
    }

    [Fact(DisplayName = "Estado com latitude que estoura a coluna: a carga conclui, o estado fica sem coordenada e os demais registros são publicados")]
    public async Task Estado_LatitudeQueEstouraAColuna_ConcluiEPublicaOsDemais()
    {
        await PublicarConcluindoAsync(FonteComEstadoNaPosicao("1234", "-50.5"));

        (await PosicaoDoEstadoAsync()).Should().Be(PosicaoGravada.Ausente);
        await ConferirDemaisRegistrosPublicadosAsync();
    }

    [Fact(DisplayName = "Estado com longitude fora do domínio e latitude válida: os dois valores ficam vazios, junto com o ponto")]
    public async Task Estado_LongitudeForaDoDominio_ZeraAmbosOsValores()
    {
        await PublicarConcluindoAsync(FonteComEstadoNaPosicao("-5.5", "-200"));

        (await PosicaoDoEstadoAsync()).Should().Be(PosicaoGravada.Ausente);
    }

    [Fact(DisplayName = "Estado com coordenada válida: grava a mesma latitude, a mesma longitude e o ponto correspondente")]
    public async Task Estado_CoordenadaValida_GravaOValorEOPonto()
    {
        await PublicarConcluindoAsync(FonteComEstadoNaPosicao("-5.5", "-50.5"));

        (await PosicaoDoEstadoAsync()).Should().Be(PosicaoGravada.Valida(-5.5m, -50.5m));
    }

    [Fact(DisplayName = "Cidade com latitude fora do domínio: a carga conclui e a cidade fica sem latitude, sem longitude e sem ponto")]
    public async Task Cidade_LatitudeForaDoDominio_FicaSemCoordenada()
    {
        await PublicarConcluindoAsync(FonteComCidadeNaPosicao("95.5", "-50.5"));

        (await PosicaoDaCidadeAsync()).Should().Be(PosicaoGravada.Ausente);
    }

    [Fact(DisplayName = "Cidade com latitude que estoura a coluna: a carga conclui, a cidade fica sem coordenada e os demais registros são publicados")]
    public async Task Cidade_LatitudeQueEstouraAColuna_ConcluiEPublicaOsDemais()
    {
        await PublicarConcluindoAsync(FonteComCidadeNaPosicao("1234", "-50.5"));

        (await PosicaoDaCidadeAsync()).Should().Be(PosicaoGravada.Ausente);
        await ConferirDemaisRegistrosPublicadosAsync();
    }

    [Fact(DisplayName = "Cidade com longitude fora do domínio e latitude válida: os dois valores ficam vazios, junto com o ponto")]
    public async Task Cidade_LongitudeForaDoDominio_ZeraAmbosOsValores()
    {
        await PublicarConcluindoAsync(FonteComCidadeNaPosicao("-5.5", "-200"));

        (await PosicaoDaCidadeAsync()).Should().Be(PosicaoGravada.Ausente);
    }

    [Fact(DisplayName = "Cidade com coordenada válida: grava a mesma latitude, a mesma longitude e o ponto correspondente")]
    public async Task Cidade_CoordenadaValida_GravaOValorEOPonto()
    {
        await PublicarConcluindoAsync(FonteComCidadeNaPosicao("-5.5", "-50.5"));

        (await PosicaoDaCidadeAsync()).Should().Be(PosicaoGravada.Valida(-5.5m, -50.5m));
    }

    [Fact(DisplayName = "Estado com mais de seis casas: a coluna guarda o arredondamento do banco e o ponto traz o valor exato enviado")]
    public async Task Estado_MaisDeSeisCasas_ColunaArredondadaEPontoExato()
    {
        await PublicarConcluindoAsync(FonteComEstadoNaPosicao("-5.3686705", "-49.1173149"));

        (await PosicaoDoEstadoAsync()).Should().Be(
            PosicaoGravada.Valida(-5.368671m, -49.117315m, -5.3686705m, -49.1173149m));
    }

    [Fact(DisplayName = "Estado com latitude logo abaixo do meio da sexta casa: a coluna guarda o decimal lido arredondado para baixo")]
    public async Task Estado_LatitudeAbaixoDoMeioDaSextaCasa_ColunaArredondadaParaBaixo()
    {
        await PublicarConcluindoAsync(FonteComEstadoNaPosicao("-5.3686704999999999", "-49.1173149"));

        (await PosicaoDoEstadoAsync()).Should().Be(
            PosicaoGravada.Valida(-5.368670m, -49.117315m, -5.3686704999999999m, -49.1173149m));
    }

    [Fact(DisplayName = "Cidade com mais de seis casas: a coluna guarda o arredondamento do banco e o ponto traz o valor exato enviado")]
    public async Task Cidade_MaisDeSeisCasas_ColunaArredondadaEPontoExato()
    {
        await PublicarConcluindoAsync(FonteComCidadeNaPosicao("-5.3686705", "-49.1173149"));

        (await PosicaoDaCidadeAsync()).Should().Be(
            PosicaoGravada.Valida(-5.368671m, -49.117315m, -5.3686705m, -49.1173149m));
    }

    [Fact(DisplayName = "Cidade com latitude logo abaixo do meio da sexta casa: a coluna guarda o decimal lido arredondado para baixo")]
    public async Task Cidade_LatitudeAbaixoDoMeioDaSextaCasa_ColunaArredondadaParaBaixo()
    {
        await PublicarConcluindoAsync(FonteComCidadeNaPosicao("-5.3686704999999999", "-49.1173149"));

        (await PosicaoDaCidadeAsync()).Should().Be(
            PosicaoGravada.Valida(-5.368670m, -49.117315m, -5.3686704999999999m, -49.1173149m));
    }

    // A carga do topo executada direto deixa a exceção do banco ("numeric field overflow") chegar ao teste.
    [Fact(DisplayName = "A gravação do estado com latitude que estoura a coluna não lança exceção do banco")]
    public async Task Estado_LatitudeQueEstouraAColuna_GravacaoNaoLancaExcecao()
    {
        await _banco.LimparAsync();

        await CargaPelaEtl.ExecutarAsync(_fixture, FonteComEstadoNaPosicao("1234", "-50.5"));

        (await PosicaoDoEstadoAsync()).Should().Be(PosicaoGravada.Ausente);
    }

    [Fact(DisplayName = "A gravação da cidade com latitude que estoura a coluna não lança exceção do banco")]
    public async Task Cidade_LatitudeQueEstouraAColuna_GravacaoNaoLancaExcecao()
    {
        await _banco.LimparAsync();

        await CargaPelaEtl.ExecutarAsync(_fixture, FonteComCidadeNaPosicao("1234", "-50.5"));

        (await PosicaoDaCidadeAsync()).Should().Be(PosicaoGravada.Ausente);
    }

    private static FonteEmMemoria FonteComEstadoNaPosicao(string latitude, string longitude)
    {
        FonteEmMemoria fonte = VersoesFicticias.Anterior();
        fonte.Estados[0] = new EstadoCruBuilder(Uf, "Pará").ComCapital("Belém")
            .NaFaixaDeCep("66000000", "68899999").NaPosicao(latitude, longitude).Construir();
        return fonte;
    }

    private static FonteEmMemoria FonteComCidadeNaPosicao(string latitude, string longitude)
    {
        FonteEmMemoria fonte = VersoesFicticias.Anterior();
        fonte.Cidades[0] = DadosDne.Cidade(CodigoDeMaraba, "Marabá", Uf, ddd: "94", latitude: latitude, longitude: longitude);
        return fonte;
    }

    private async Task PublicarConcluindoAsync(FonteEmMemoria fonte)
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);

        Guid id = await montagem.PublicarAsync(fonte);

        GeoImportacaoExecucao execucao = await _banco.ExecucaoAsync(id);
        execucao.Status.Should().Be(
            StatusImportacao.Concluida,
            "coordenada suja não derruba a carga; mensagem da execução: {0}; registro da carga: {1}",
            execucao.Mensagem,
            montagem.Logs.TextoCompleto);
    }

    private async Task ConferirDemaisRegistrosPublicadosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Estados.CountAsync()).Should().Be(2);
        (await leitura.Cidades.CountAsync()).Should().Be(2);
        (await leitura.Logradouros.CountAsync()).Should().Be(3);
    }

    private async Task<PosicaoGravada> PosicaoDoEstadoAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Estado estado = await leitura.Estados.SingleAsync(e => e.Uf == Uf);
        return PosicaoGravada.De(estado.Latitude, estado.Longitude, estado.Coordenada);
    }

    private async Task<PosicaoGravada> PosicaoDaCidadeAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Cidade cidade = await leitura.Cidades.SingleAsync(c => c.CodigoIbge == CodigoDeMaraba);
        return PosicaoGravada.De(cidade.Latitude, cidade.Longitude, cidade.Coordenada);
    }
}
