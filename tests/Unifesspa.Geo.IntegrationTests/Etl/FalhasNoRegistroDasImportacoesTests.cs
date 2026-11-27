namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Runtime.Versioning;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Admin;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Falhas no meio do pedido de envio: a recusa por carga em andamento vem antes de ler o corpo e
/// nenhuma exceção deixa a trava da carga presa nem o arquivo para trás.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class FalhasNoRegistroDasImportacoesTests : IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly EnvioDoPacoteParaTeste _envio = new();

    public FalhasNoRegistroDasImportacoesTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Com carga em andamento e a trava ocupada, o pedido é recusado sem ler o corpo e sem deixar arquivo")]
    public async Task ComCargaEmAndamento_RecusaSemLerOCorpo()
    {
        await _banco.LimparAsync();
        await new ExecucoesSemeadasParaOEnvio(_fixture).EmAndamentoAsync("202610", _envio.Receber("202610").Bytes);
        using FluxoComGancho corpo = new(_envio.Receber("202611").Bytes, () => throw new InvalidOperationException("o corpo foi lido"));

        Result<EnvioDoPacoteRegistrado> resultado;
        await using (await TravaDaCargaParaTeste.AdquirirAsync(_fixture.ConnectionString))
        {
            resultado = await IniciarAsync(corpo, TimeProvider.System);
        }

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.ImportacaoEmAndamento);
        _envio.NomesDosArquivos().Should().BeEmpty();
        await ConferirTravaLivreAsync();
    }

    [Fact(DisplayName = "Exceção ao criar a execução, depois de obter a trava, solta a trava e apaga o arquivo")]
    public async Task ExcecaoAoCriarAExecucao_SoltaATravaEApagaOArquivo()
    {
        await _banco.LimparAsync();
        Func<Task> pedido = async () =>
        {
            using MemoryStream corpo = _envio.Receber("202611").Abrir();
            await IniciarAsync(corpo, new RelogioQueLanca());
        };

        await pedido.Should().ThrowAsync<InvalidOperationException>();
        await ConferirTravaLivreAsync();
        _envio.NomesDosArquivos().Should().BeEmpty();
    }

    [Fact(DisplayName = "Falha ao apagar o arquivo de uma recusa solta a trava da carga")]
    [UnsupportedOSPlatform("windows")]
    public async Task FalhaAoApagarOArquivo_SoltaATrava()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return;
        }

        await _banco.LimparAsync();
        Func<Task> pedido = PedidoQueFalhaAoApagarOArquivo();

        await ConferirRecusaPorFaltaDePermissaoNoDiretorioAsync(pedido);

        await ConferirTravaLivreAsync();
    }

    [Fact(DisplayName = "Pedido cancelado durante o registro termina com a execução registrada e enfileirada, ou com nada")]
    public async Task PedidoCanceladoDuranteORegistro_RegistraEEnfileiraOuNaoFazNada()
    {
        await _banco.LimparAsync();
        using CancellationTokenSource pedido = new();
        GeoImportacaoFila fila = new();
        using MemoryStream corpo = _envio.Receber("202611").Abrir();

        Result<EnvioDoPacoteRegistrado> resultado = await IniciarAsync(corpo, TimeProvider.System, new CancelaOPedidoAoSalvar(pedido), fila, pedido.Token);

        resultado.IsSuccess.Should().BeTrue("depois de obtida a trava, o registro não é cancelável pelo pedido");
        IReadOnlyList<CargaReservada> enfileiradas = fila.DrenarRestante();
        enfileiradas.Should().ContainSingle().Which.ExecucaoId.Should().Be(resultado.Value!.ExecucaoId);
        (await _banco.ExecucaoAsync(resultado.Value.ExecucaoId)).Status.Should().Be(StatusImportacao.EmAndamento);
        await enfileiradas[0].DisposeAsync();
        await ConferirTravaLivreAsync();
    }

    [UnsupportedOSPlatform("windows")]
    private Func<Task> PedidoQueFalhaAoApagarOArquivo()
    {
        byte[] outroPacote = _envio.Receber("202610").Bytes;
        return async () =>
        {
            using FluxoComGancho corpo = new(
                _envio.Receber("202611").Bytes,
                () => new ExecucoesSemeadasParaOEnvio(_fixture).EmAndamentoAsync("202610", outroPacote));
            await IniciarAsync(corpo, TimeProvider.System, new ProibeEscritaNoDiretorioAoSalvar(_envio.Diretorio));
        };
    }

    [UnsupportedOSPlatform("windows")]
    private async Task ConferirRecusaPorFaltaDePermissaoNoDiretorioAsync(Func<Task> pedido)
    {
        try
        {
            await pedido.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        finally
        {
            ProibeEscritaNoDiretorioAoSalvar.Restaurar(_envio.Diretorio);
        }
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> IniciarAsync(
        Stream corpo,
        TimeProvider relogio,
        SaveChangesInterceptor? interceptador = null,
        GeoImportacaoFila? fila = null,
        CancellationToken cancellationToken = default)
    {
        await using GeoDbContext contexto = interceptador is null
            ? _fixture.CreateDbContext()
            : _fixture.CreateDbContext([interceptador]);
        RegistroDasImportacoes registro = _envio.CriarRegistro(
            contexto,
            fila ?? new GeoImportacaoFila(),
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            relogio,
            NullLogger<RegistroDasImportacoes>.Instance);

        return await registro.IniciarAsync(corpo, "teste", cancellationToken);
    }

    private async Task ConferirTravaLivreAsync()
    {
        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull("nenhuma falha pode deixar a trava da carga presa");
        await trava!.DisposeAsync();
    }

    private sealed class CancelaOPedidoAoSalvar : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource _pedido;

        public CancelaOPedidoAoSalvar(CancellationTokenSource pedido)
        {
            _pedido = pedido;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await _pedido.CancelAsync();
            return result;
        }
    }

    private sealed class RelogioQueLanca : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Falha simulada do relógio.");
    }

    [UnsupportedOSPlatform("windows")]
    private sealed class ProibeEscritaNoDiretorioAoSalvar : SaveChangesInterceptor
    {
        private readonly string _diretorio;

        public ProibeEscritaNoDiretorioAoSalvar(string diretorio)
        {
            _diretorio = diretorio;
        }

        public static void Restaurar(string diretorio) =>
            File.SetUnixFileMode(diretorio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            File.SetUnixFileMode(_diretorio, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return ValueTask.FromResult(result);
        }
    }
}
