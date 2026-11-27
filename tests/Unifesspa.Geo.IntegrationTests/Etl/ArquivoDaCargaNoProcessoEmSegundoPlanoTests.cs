namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Runtime.Versioning;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Quando a execução é marcada como falha sem passar pelo executor (erro ao criar o escopo ou
/// resolver o executor, itens drenados no desligamento), o arquivo do pacote também é apagado e a
/// trava é solta, mesmo que a remoção ou a marcação falhem.
/// </summary>
public sealed class ArquivoDaCargaNoProcessoEmSegundoPlanoTests : IDisposable
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(30);

    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"processo-pacote-{Guid.NewGuid():N}");
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly GeoImportacaoFila _fila = new();
    private readonly ExecutorDeTeste _executor = new();
    private readonly TravaFalsa _trava = new();
    private readonly Guid _execucao = Guid.NewGuid();

    public ArquivoDaCargaNoProcessoEmSegundoPlanoTests()
    {
        _arquivos = new ArquivoDoPacoteDne(_diretorio);
        Directory.CreateDirectory(_diretorio);
        File.WriteAllBytes(_arquivos.CaminhoDe(_execucao), [1, 2, 3]);
    }

    public void Dispose()
    {
        _trava.DisposeAsync().AsTask().Wait();
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(_diretorio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(_diretorio, recursive: true);
    }

    [Fact(DisplayName = "Erro ao criar o escopo da execução apaga o arquivo e solta a trava")]
    public async Task ErroAoCriarOEscopo_ApagaOArquivoESoltaATrava()
    {
        IServiceScopeFactory escopos = Substitute.For<IServiceScopeFactory>();
        escopos.CreateScope().Returns(_ => throw new InvalidOperationException("falha simulada do escopo"));

        await ProcessarUmaCargaAsync(escopos);

        ArquivoFoiApagado().Should().BeTrue();
    }

    [Fact(DisplayName = "Executor que lança exceção deixa o arquivo apagado e a trava solta")]
    public async Task ExecutorQueLanca_ApagaOArquivo()
    {
        _executor.FalhaAoExecutar = true;

        await ProcessarUmaCargaAsync(EscopoComExecutor());

        ArquivoFoiApagado().Should().BeTrue();
    }

    [Fact(DisplayName = "Desligamento apaga o arquivo das cargas drenadas e solta a trava")]
    public async Task Desligamento_ApagaOArquivoDasCargasDrenadas()
    {
        using GeoImportacaoBackgroundService processo = CriarProcesso(EscopoComExecutor());
        await _fila.EnfileirarAsync(CargaReservada.Reservar(_execucao, _trava), CancellationToken.None);

        await processo.StopAsync(CancellationToken.None);

        ArquivoFoiApagado().Should().BeTrue();
        _trava.Solta.Should().BeTrue();
    }

    [Fact(DisplayName = "Desligamento com falha ao marcar a execução ainda apaga o arquivo e solta a trava")]
    public async Task DesligamentoComFalhaAoMarcar_ApagaOArquivoESoltaATrava()
    {
        _executor.FalhaAoMarcarInterrompida = true;
        using GeoImportacaoBackgroundService processo = CriarProcesso(EscopoComExecutor());
        await _fila.EnfileirarAsync(CargaReservada.Reservar(_execucao, _trava), CancellationToken.None);

        await processo.StopAsync(CancellationToken.None);

        ArquivoFoiApagado().Should().BeTrue();
        _trava.Solta.Should().BeTrue();
    }

    [Fact(DisplayName = "Falha ao apagar o arquivo não esconde o erro da execução nem prende a trava")]
    [UnsupportedOSPlatform("windows")]
    public async Task FalhaAoApagar_NaoPrendeATrava()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return;
        }

        _executor.FalhaAoExecutar = true;
        File.SetUnixFileMode(_diretorio, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        await ProcessarUmaCargaAsync(EscopoComExecutor());

        _trava.Solta.Should().BeTrue();
        File.Exists(_arquivos.CaminhoDe(_execucao)).Should().BeTrue("a remoção falhou, mas a falha foi tolerada");
    }

    private bool ArquivoFoiApagado() => !File.Exists(_arquivos.CaminhoDe(_execucao));

    private IServiceScopeFactory EscopoComExecutor()
    {
        IServiceProvider provedor = Substitute.For<IServiceProvider>();
        provedor.GetService(typeof(IGeoImportacaoExecutor)).Returns(_executor);
        IServiceScope escopo = Substitute.For<IServiceScope>();
        escopo.ServiceProvider.Returns(provedor);
        IServiceScopeFactory escopos = Substitute.For<IServiceScopeFactory>();
        escopos.CreateScope().Returns(escopo);
        return escopos;
    }

    private GeoImportacaoBackgroundService CriarProcesso(IServiceScopeFactory escopos) =>
        new(escopos, _fila, _arquivos, NullLogger<GeoImportacaoBackgroundService>.Instance);

    private async Task ProcessarUmaCargaAsync(IServiceScopeFactory escopos)
    {
        using GeoImportacaoBackgroundService processo = CriarProcesso(escopos);
        await _fila.EnfileirarAsync(CargaReservada.Reservar(_execucao, _trava), CancellationToken.None);
        await processo.StartAsync(CancellationToken.None);
        await _trava.AguardarSoltaAsync().WaitAsync(Prazo);
        await processo.StopAsync(CancellationToken.None);
    }

    private sealed class ExecutorDeTeste : IGeoImportacaoExecutor
    {
        public bool FalhaAoExecutar { get; set; }

        public bool FalhaAoMarcarInterrompida { get; set; }

        public Task ExecutarAsync(CargaReservada carga, CancellationToken cancellationToken) =>
            FalhaAoExecutar ? throw new InvalidOperationException("falha simulada") : Task.CompletedTask;

        public Task MarcarInterrompidaNoDesligamentoAsync(Guid execucaoId, CancellationToken cancellationToken) =>
            FalhaAoMarcarInterrompida ? throw new InvalidOperationException("falha simulada") : Task.CompletedTask;

        public Task MarcarFalhaInesperadaAsync(Guid execucaoId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TravaFalsa : IAsyncDisposable
    {
        private readonly TaskCompletionSource _solta = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Solta => _solta.Task.IsCompleted;

        public Task AguardarSoltaAsync() => _solta.Task;

        public ValueTask DisposeAsync()
        {
            _solta.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
