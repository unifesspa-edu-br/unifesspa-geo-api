namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Data.Common;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Readers;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// A carga publica tudo numa única transação do banco e grava a coordenada como
/// <c>geography(Point,4326)</c> pela cópia binária, relida pelos leitores de CEP e de proximidade.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CargaEmTransacaoUnicaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public CargaEmTransacaoUnicaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Topo, folhas, cópia em lote e conclusão da execução usam a mesma transação e só ficam visíveis na confirmação")]
    public async Task TopoFolhasCopiaEConclusao_UsamAMesmaTransacao()
    {
        await _banco.LimparAsync();
        List<ObservacaoDaTransacao> observacoes = [];
        ObservadorDaConfirmacao confirmacao = new(async transacao => observacoes.Add(await ObservarNaConfirmacaoAsync(transacao)));
        await using GeoDbContext contexto = _fixture.CreateDbContext([confirmacao]);
        using MontagemDaCarga montagem = MontagemComObservacaoDoTopoEDasFolhas(contexto, observacoes);
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoAnterior);

        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(VersoesFicticias.Anterior()), contexto);

        observacoes.Should().HaveCount(3, "topo, folhas (com a cópia em lote) e confirmação");
        observacoes.Select(o => o.Transacao).Distinct(StringComparer.Ordinal).Should().HaveCount(1);
        observacoes.Should().OnlyContain(o => o.LinhasVisiveisParaOutraConexao == 0, "nada é visível fora da transação antes do commit");
        await ConferirPublicadoAposConfirmacaoAsync(id);
    }

    [Fact(DisplayName = "A coordenada copiada em lote é relida como ponto geográfico 4326 pelo leitor de CEP e pelo de proximidade")]
    public async Task CoordenadaCopiada_ERelidaPeloCepEPelaProximidade()
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);
        await montagem.PublicarAsync(VersoesFicticias.Anterior());

        await ConferirPontoGravadoAsync();
        await ConferirLeituraDoCepAsync();
        await ConferirLeituraDeProximidadeAsync();
    }

    private MontagemDaCarga MontagemComObservacaoDoTopoEDasFolhas(GeoDbContext contexto, List<ObservacaoDaTransacao> observacoes) =>
        new(_fixture)
        {
            DecorarTopo = topo => new TopoComGancho(topo, async () => observacoes.Add(await ObservarAsync(contexto))),
            DecorarFolhas = folhas => new FolhasComGancho(folhas, async () => observacoes.Add(await ObservarAsync(contexto))),
        };

    private async Task<ObservacaoDaTransacao> ObservarAsync(GeoDbContext contexto)
    {
        string transacao = await contexto.Database
            .SqlQueryRaw<string>("SELECT pg_current_xact_id()::text AS \"Value\"")
            .SingleAsync();
        return new ObservacaoDaTransacao(transacao, await _banco.LinhasVisiveisParaOutraConexaoAsync());
    }

    private async Task<ObservacaoDaTransacao> ObservarNaConfirmacaoAsync(DbTransaction transacao)
    {
        await using DbCommand comando = transacao.Connection!.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT pg_current_xact_id()::text";
        string xid = (string)(await comando.ExecuteScalarAsync())!;
        return new ObservacaoDaTransacao(xid, await _banco.LinhasVisiveisParaOutraConexaoAsync());
    }

    private async Task ConferirPublicadoAposConfirmacaoAsync(Guid execucaoId)
    {
        (await _banco.ExecucaoAsync(execucaoId)).Status.Should().Be(StatusImportacao.Concluida);
        (await _banco.LinhasVisiveisParaOutraConexaoAsync()).Should().BeGreaterThan(1, "após o commit tudo fica visível de uma vez");
    }

    private async Task ConferirPontoGravadoAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Point? ponto = (await leitura.Logradouros.AsNoTracking().SingleAsync(l => l.Cep == VersoesFicticias.CepRuaA)).Coordenada;

        ponto.Should().NotBeNull();
        ponto!.SRID.Should().Be(4326);
        ponto.X.Should().BeApproximately(VersoesFicticias.LongitudeRuaA, 1e-9);
        ponto.Y.Should().BeApproximately(VersoesFicticias.LatitudeRuaA, 1e-9);
    }

    private async Task ConferirLeituraDoCepAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        CepReader leitor = new(leitura, Options.Create(new GeoCepLookupOptions()), NullLogger<CepReader>.Instance);

        CepResolvidoDto? cep = await leitor.ResolverAsync(VersoesFicticias.CepRuaA, CancellationToken.None);

        cep.Should().NotBeNull();
        cep!.Logradouro.Should().Be("Rua A");
        cep.Latitude.Should().Be(-5.36867m);
        cep.Longitude.Should().Be(-49.11731m);
    }

    private async Task ConferirLeituraDeProximidadeAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoProximidadeReader leitor = new(leitura);

        IReadOnlyList<LogradouroProximoDto> perto = await leitor.BuscarLogradourosProximosAsync(
            VersoesFicticias.LatitudeRuaA, VersoesFicticias.LongitudeRuaA, 1, 10, CancellationToken.None);
        IReadOnlyList<LogradouroProximoDto> longe = await leitor.BuscarLogradourosProximosAsync(-23.55, -46.63, 1, 10, CancellationToken.None);

        perto.Should().ContainSingle(l => l.Cep == VersoesFicticias.CepRuaA).Which.DistanciaKm.Should().BeLessThan(0.001);
        longe.Should().BeEmpty("nenhum logradouro carregado está a menos de 1 km do ponto distante");
    }
}

internal sealed record ObservacaoDaTransacao(string Transacao, long LinhasVisiveisParaOutraConexao);
