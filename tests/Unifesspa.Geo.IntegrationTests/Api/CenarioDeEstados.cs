namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio dos testes de estados: apaga o que existe e grava o Brasil com os estados de cada
/// cenário. Usado por composição.
/// </summary>
internal sealed class CenarioDeEstados
{
    private readonly GeoPostgisFixture _fixture;

    public CenarioDeEstados(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public Task SemearEstadosAsync(IReadOnlyList<string> ufs) =>
        GravarAsync(paisId => ufs.Select(uf => new EstadoBuilder(paisId, uf, $"Estado {uf}")));

    // Nomes cuja ordem alfabética (Acre < Bahia < Minas Gerais < Pará < Sergipe) difere
    // da ordem de inserção — prova que a ordenação é por nome, não por Id/inserção.
    public Task SemearForaDeOrdemAsync() =>
        GravarAsync(paisId =>
        [
            new EstadoBuilder(paisId, "SE", "Sergipe"),
            new EstadoBuilder(paisId, "AC", "Acre"),
            new EstadoBuilder(paisId, "BA", "Bahia"),
            new EstadoBuilder(paisId, "MG", "Minas Gerais"),
            new EstadoBuilder(paisId, "PA", "Pará"),
        ]);

    public Task SemearParaAsync() =>
        GravarAsync(paisId =>
        [
            new EstadoBuilder(paisId, "PA", "Pará")
                .NaRegiao("Norte").ComCapital("Belém").ComCodigoIbge("15").NaPosicao(-3.79m, -52.48m),
        ]);

    public Task GravarEstadoVigenteEObsoletoAsync() =>
        GravarAsync(paisId =>
        [
            new EstadoBuilder(paisId, "PA", "Pará"),
            new EstadoBuilder(paisId, "ZZ", "Obsoleto").ForaDeVigencia(),
        ]);

    private async Task GravarAsync(Func<Guid, IEnumerable<EstadoBuilder>> estados)
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        ctx.Paises.Add(brasil);
        ctx.Estados.AddRange(estados(brasil.Id).Select(estado => estado.Construir()));
        await ctx.SaveChangesAsync();
    }
}
