namespace Unifesspa.Geo.IntegrationTests.Persistencia;

using AwesomeAssertions;

using Etl;

using Infrastructure;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Prova que a criação e a alteração de um registro carimbam <c>CreatedAt</c> e
/// <c>UpdatedAt</c> com o instante do relógio informado ao contexto, e não com o do sistema.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CarimboDeAuditoriaTests
{
    private static readonly TimeSpan PrazoAteAlteracao = TimeSpan.FromHours(3);

    private readonly GeoPostgisFixture _fixture;
    private readonly RelogioFixo _relogio = new();

    public CarimboDeAuditoriaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Ao criar um registro, a data de criação vem do relógio e a de alteração fica vazia")]
    public async Task Criar_CarimbaACriacaoComORelogio()
    {
        DateTimeOffset instanteDaCriacao = _relogio.GetUtcNow();
        Pais pais = NovoPais();

        await GravarAsync(pais);

        Pais lido = await LerAsync(pais.Id);
        lido.CreatedAt.Should().Be(instanteDaCriacao);
        lido.UpdatedAt.Should().BeNull();
    }

    [Fact(DisplayName = "Ao alterar um registro, a data de alteração vem do relógio e a de criação não muda")]
    public async Task Alterar_CarimbaAAlteracaoComORelogioEMantemACriacao()
    {
        DateTimeOffset instanteDaCriacao = _relogio.GetUtcNow();
        Pais pais = NovoPais();
        await GravarAsync(pais);
        _relogio.Avancar(PrazoAteAlteracao);

        await RenomearAsync(pais.Id, "Brasil renomeado");

        Pais lido = await LerAsync(pais.Id);
        lido.Nome.Should().Be("Brasil renomeado");
        lido.CreatedAt.Should().Be(instanteDaCriacao);
        lido.UpdatedAt.Should().Be(instanteDaCriacao + PrazoAteAlteracao);
    }

    private static Pais NovoPais() =>
        GeoTestKeys.Ok(Pais.Importar(
            siglaIso: GeoTestKeys.SiglaIso(), sigla: "BR", nome: "Brasil",
            codigos: default, proveniencia: new ProvenienciaDaCarga("202601")));

    private async Task GravarAsync(Pais pais)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext(_relogio);
        contexto.Add(pais);
        await contexto.SaveChangesAsync();
    }

    private async Task RenomearAsync(Guid id, string nome)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext(_relogio);
        Pais pais = await contexto.Paises.SingleAsync(p => p.Id == id);
        pais.Atualizar(pais.Sigla, nome, default, new ProvenienciaDaCarga("202601")).IsSuccess.Should().BeTrue();
        await contexto.SaveChangesAsync();
    }

    private async Task<Pais> LerAsync(Guid id)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return await leitura.Paises.SingleAsync(p => p.Id == id);
    }
}
