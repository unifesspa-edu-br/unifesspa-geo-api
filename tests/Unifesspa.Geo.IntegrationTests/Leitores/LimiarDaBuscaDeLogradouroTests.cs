namespace Unifesspa.Geo.IntegrationTests.Leitores;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Readers;
using Unifesspa.Geo.IntegrationTests.Api;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Busca de logradouro por relevância no leitor, direto sobre o PostGIS: a busca fixa o próprio limiar
/// de similaridade de palavras (0,6) só dentro da transação e deixa o limiar da sessão como estava. A
/// collection é serial; cada teste apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class LimiarDaBuscaDeLogradouroTests
{
    // Casa "Avenida Paulista" com similaridade de palavras entre 0,6 e 0,9.
    private const string Termo = "av paulista";
    private const string LimiarDaBusca = "0.6";
    private const string LimiarDaSessao = "0.9";

    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeMarabaParaAsConsultas _cenario;

    public LimiarDaBuscaDeLogradouroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeMarabaParaAsConsultas(fixture);
    }

    [Fact(DisplayName = "Com o limiar da sessão em 0,9, termo que só casa a 0,6 é achado pela busca")]
    public async Task Busca_UsaOLimiarDaBuscaEmVezDoDaSessao()
    {
        await _cenario.GravarAsync(AdicionarAvenidaPaulista);
        await using GeoDbContext ctx = await AbrirComLimiarDaSessaoAsync();

        (await SimilaridadeDoTermoAsync(ctx)).Should().BeInRange(0.6, 0.89);
        (_, IReadOnlyList<LogradouroComBairro> itens, _, _) = await BuscarAsync(ctx);

        itens.Should().ContainSingle().Which.Logradouro.Nome.Should().Be("Avenida Paulista");
    }

    [Fact(DisplayName = "Depois da busca, o limiar da sessão continua o de antes")]
    public async Task Busca_DeixaOLimiarDaSessaoComoEstava()
    {
        await _cenario.GravarAsync(AdicionarAvenidaPaulista);
        await using GeoDbContext ctx = await AbrirComLimiarDaSessaoAsync();

        await BuscarAsync(ctx);

        (await LimiarAtualAsync(ctx)).Should().Be(LimiarDaSessao);
        ctx.Database.CurrentTransaction.Should().BeNull("a transação aberta pela busca é confirmada e descartada");
    }

    [Fact(DisplayName = "Busca em transação aberta pelo chamador a deixa viva e sem confirmar")]
    public async Task Busca_EmTransacaoDoChamador_DeixaATransacaoViva()
    {
        await _cenario.GravarAsync(AdicionarAvenidaPaulista);
        await using GeoDbContext ctx = await AbrirComLimiarDaSessaoAsync();
        await using IDbContextTransaction transacao = await ctx.Database.BeginTransactionAsync();

        (_, IReadOnlyList<LogradouroComBairro> itens, _, _) = await BuscarAsync(ctx);

        itens.Should().ContainSingle();
        ctx.Database.CurrentTransaction.Should().BeSameAs(transacao);
        (await LimiarAtualAsync(ctx)).Should().Be(LimiarDaBusca, "a transação do chamador não foi confirmada");
        await transacao.RollbackAsync();
        (await LimiarAtualAsync(ctx)).Should().Be(LimiarDaSessao);
    }

    private static void AdicionarAvenidaPaulista(GeoDbContext ctx, Cidade cidade) =>
        ctx.Logradouros.Add(CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68501000", "Avenida Paulista", "Avenida"));

    // Mantém a conexão aberta para que o SET de sessão valha para as consultas seguintes.
    private async Task<GeoDbContext> AbrirComLimiarDaSessaoAsync()
    {
        GeoDbContext ctx = _fixture.CreateDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET pg_trgm.word_similarity_threshold = 0.9");
        return ctx;
    }

    private static Task<(bool CidadeExiste, IReadOnlyList<LogradouroComBairro> Itens, Guid? AnteriorAfterId, Guid? ProximoAfterId)> BuscarAsync(GeoDbContext ctx) =>
        new LogradouroReader(ctx).ListarPorCidadeAsync(
            CenarioDeMarabaParaAsConsultas.CodigoIbgeDeMaraba, null, 10, PaginationDirection.Next, Termo, CancellationToken.None);

    private static Task<string> LimiarAtualAsync(GeoDbContext ctx) =>
        ctx.Database
            .SqlQueryRaw<string>("SELECT current_setting('pg_trgm.word_similarity_threshold') AS \"Value\"")
            .SingleAsync();

    private static Task<double> SimilaridadeDoTermoAsync(GeoDbContext ctx) =>
        ctx.Database
            .SqlQueryRaw<double>("SELECT word_similarity('av paulista', 'avenida paulista')::float8 AS \"Value\"")
            .SingleAsync();
}
