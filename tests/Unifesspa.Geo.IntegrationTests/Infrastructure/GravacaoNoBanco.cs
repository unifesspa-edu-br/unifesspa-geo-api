namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Grava entidades no banco compartilhado da coleção, cada gravação em um contexto
/// próprio, e prova que uma duplicata foi barrada pela constraint única esperada.
/// </summary>
internal sealed class GravacaoNoBanco
{
    private readonly GeoPostgisFixture _fixture;

    public GravacaoNoBanco(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task GravarAsync(params object[] entidades)
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        ctx.AddRange(entidades);
        await ctx.SaveChangesAsync();
    }

    public async Task ExigirViolacaoUniqueAsync(object entidadeDuplicada, string constraintEsperada)
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        ctx.Add(entidadeDuplicada);
        Exception excecao = (await ((Func<Task>)(() => ctx.SaveChangesAsync())).Should().ThrowAsync<DbUpdateException>()).Which;
        GeoTestKeys.DeveSerViolacaoUnique(excecao, constraintEsperada);
    }
}
