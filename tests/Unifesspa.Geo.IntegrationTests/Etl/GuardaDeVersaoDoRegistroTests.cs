namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// A guarda de versão progressiva só considera as execuções concluídas e os dados publicados:
/// uma execução que falhou, mesmo de versão futura, não eleva o piso.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class GuardaDeVersaoDoRegistroTests
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public GuardaDeVersaoDoRegistroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Execução falha de versão futura (202612) não bloqueia a versão intermediária válida (202611)")]
    public async Task ExecucaoFalhaDeVersaoFutura_NaoBloqueiaVersaoIntermediaria()
    {
        await PrepararHistoricoAsync();

        Result<Guid> resultado = await IniciarAsync("202611");

        resultado.IsSuccess.Should().BeTrue("a falha de 202612 não conta como versão aplicada");
    }

    [Fact(DisplayName = "Versão anterior à última concluída (202610) continua recusada como não progressiva")]
    public async Task VersaoAnteriorAUltimaConcluida_ERecusada()
    {
        await PrepararHistoricoAsync();

        Result<Guid> resultado = await IniciarAsync("202609");

        resultado.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.VersaoNaoProgressiva);
    }

    private async Task PrepararHistoricoAsync()
    {
        await _banco.LimparAsync();
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao concluida = GeoImportacaoExecucao.Iniciar("202610", "teste", Instante).Value!;
        concluida.Concluir(Instante.AddMinutes(5), "{}", "concluída");
        GeoImportacaoExecucao falha = GeoImportacaoExecucao.Iniciar("202612", "teste", Instante.AddDays(1)).Value!;
        falha.Falhar(Instante.AddDays(1).AddMinutes(5), "falha simulada");
        contexto.ImportacaoExecucoes.AddRange(concluida, falha);
        await contexto.SaveChangesAsync();
    }

    private async Task<Result<Guid>> IniciarAsync(string versao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoFila fila = new();
        RegistroDasImportacoes registro = new(
            contexto,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            TimeProvider.System,
            NullLogger<RegistroDasImportacoes>.Instance);
        Result<Guid> resultado = await registro.IniciarAsync(versao, "teste", CancellationToken.None);
        foreach (CargaReservada enfileirada in fila.DrenarRestante())
        {
            await enfileirada.DisposeAsync();
        }

        return resultado;
    }
}
