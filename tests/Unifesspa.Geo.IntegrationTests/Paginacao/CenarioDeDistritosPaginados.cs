namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Api;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Apoio dos testes de navegação: grava Marabá com a quantidade pedida de distritos. Usado por composição.</summary>
internal sealed class CenarioDeDistritosPaginados
{
    public const string CodigoIbgeDeMaraba = "1500402";

    private readonly GeoPostgisFixture _fixture;

    public CenarioDeDistritosPaginados(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task SemearAsync(int quantidadeDeDistritos)
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado para = new EstadoBuilder(brasil.Id, "PA", "Pará").Construir();
        Cidade maraba = new CidadeBuilder(para.Id, "PA", CodigoIbgeDeMaraba, "Marabá").Construir();
        ctx.Paises.Add(brasil);
        ctx.Estados.Add(para);
        ctx.Cidades.Add(maraba);
        ctx.Distritos.AddRange(Enumerable.Range(1, quantidadeDeDistritos)
            .Select(numero => GeoReferenceSeed.NovoDistrito(maraba.Id, "PA", $"Distrito {numero}")));
        await ctx.SaveChangesAsync();
    }
}
