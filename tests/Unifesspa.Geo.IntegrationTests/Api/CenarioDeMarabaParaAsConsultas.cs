namespace Unifesspa.Geo.IntegrationTests.Api;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio dos testes de consulta: apaga o que existe e grava o Pará com Marabá (código IBGE 1500402,
/// com posição e ponto), deixando ao teste acrescentar o restante antes da gravação. Usado por
/// composição.
/// </summary>
internal sealed class CenarioDeMarabaParaAsConsultas
{
    public const string CodigoIbgeDeMaraba = "1500402";

    private readonly GeoPostgisFixture _fixture;

    public CenarioDeMarabaParaAsConsultas(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Grava Marabá vigente e o que o teste acrescentar a partir da cidade.</summary>
    public Task GravarAsync(Action<GeoDbContext, Cidade> acrescentar) =>
        GravarAsync(acrescentar, cidade => cidade);

    /// <summary>Grava Marabá fora de vigência e o que o teste acrescentar a partir da cidade.</summary>
    public Task GravarComCidadeForaDeVigenciaAsync(Action<GeoDbContext, Cidade> acrescentar) =>
        GravarAsync(acrescentar, cidade => cidade.ForaDeVigencia());

    /// <summary>Logradouro de Marabá, com distrito e bairro opcionais.</summary>
    public static Logradouro LogradouroEm(
        Cidade cidade, string cep, string nome, string tipo, Distrito? distrito = null, Bairro? bairro = null)
    {
        ArgumentNullException.ThrowIfNull(cidade);

        LogradouroBuilder builder = new LogradouroBuilder(cidade.Id, cidade.Uf, cep, nome).DoTipo(tipo);
        if (distrito is not null)
        {
            builder.NoDistrito(distrito.Id);
        }

        if (bairro is not null)
        {
            builder.NoBairro(bairro.Id);
        }

        return builder.Construir();
    }

    private async Task GravarAsync(
        Action<GeoDbContext, Cidade> acrescentar, Func<CidadeBuilder, CidadeBuilder> ajustar)
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado para = new EstadoBuilder(brasil.Id, "PA", "Pará").Construir();
        Cidade maraba = ajustar(CidadeDeMaraba(para.Id)).Construir();
        ctx.Paises.Add(brasil);
        ctx.Estados.Add(para);
        ctx.Cidades.Add(maraba);
        acrescentar(ctx, maraba);
        await ctx.SaveChangesAsync();
    }

    private static CidadeBuilder CidadeDeMaraba(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", CodigoIbgeDeMaraba, "Marabá")
            .NaPosicao(-5.36867m, -49.11731m)
            .ComCoordenada(new Point(-49.11731, -5.36867) { SRID = 4326 });
}
