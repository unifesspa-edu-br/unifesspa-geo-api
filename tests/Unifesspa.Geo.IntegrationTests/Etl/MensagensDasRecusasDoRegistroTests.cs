namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Fixa o código e o texto exatos das duas recusas do envio do pacote que o registro das
/// importações monta: versão anterior à última aplicada e serviço em desligamento.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class MensagensDasRecusasDoRegistroTests : IDisposable
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly EnvioDoPacoteParaTeste _envio = new();

    public MensagensDasRecusasDoRegistroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Versão anterior à última aplicada é recusada com o texto que cita as duas versões")]
    public async Task VersaoNaoProgressiva_TrazAsDuasVersoesNoTexto()
    {
        await SemearExecucaoConcluidaAsync("202610");

        Result<EnvioDoPacoteRegistrado> resultado = await _envio.EnviarAsync(_fixture, _envio.Receber("202609"));

        resultado.Error.Should().Be(new DomainError(
            GeoImportacaoErrorCodes.VersaoNaoProgressiva,
            "A versão 202609 é anterior à última aplicada (202610)."));
    }

    [Fact(DisplayName = "Fila fechada no desligamento recusa o envio com o texto de carga não enfileirada")]
    public async Task FilaFechada_RecusaComOTextoDeNaoEnfileirada()
    {
        await _banco.LimparAsync();
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        RegistroDasImportacoes registro = CriarRegistroComFilaFechada(contexto);

        Result<EnvioDoPacoteRegistrado> resultado = await registro.IniciarAsync(_envio.Receber("202601").Abrir(), "teste", CancellationToken.None);

        resultado.Error.Should().Be(new DomainError(
            GeoImportacaoErrorCodes.NaoEnfileirada,
            "Não foi possível colocar a carga na fila porque o serviço está sendo encerrado."));
    }

    private RegistroDasImportacoes CriarRegistroComFilaFechada(GeoDbContext contexto)
    {
        GeoImportacaoFila fila = new();
        fila.Completar();
        return _envio.CriarRegistro(
            contexto,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            TimeProvider.System,
            NullLogger<RegistroDasImportacoes>.Instance);
    }

    private async Task SemearExecucaoConcluidaAsync(string versao)
    {
        await _banco.LimparAsync();
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao concluida = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido(versao), "teste", Instante).Value!;
        concluida.Concluir(Instante.AddMinutes(5), "{}", "concluída");
        contexto.ImportacaoExecucoes.Add(concluida);
        await contexto.SaveChangesAsync();
    }
}
