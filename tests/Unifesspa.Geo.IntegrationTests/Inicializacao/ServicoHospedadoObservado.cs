namespace Unifesspa.Geo.IntegrationTests.Inicializacao;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Envolve um serviço hospedado da API. Se ele é um passo da partida, só anota o começo e o
/// término, sem iniciar o serviço de verdade (nada toca o banco); o intervalo entre as duas
/// anotações espera um instante, para que partidas simultâneas se misturem nas anotações. Os demais
/// serviços partem normalmente.
/// </summary>
internal sealed class ServicoHospedadoObservado : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly IHostedService _original;
    private readonly PassoDaInicializacao? _passo;
    private readonly OrdemObservadaDaInicializacao _ordem;
    private readonly bool _descartaOriginal;
    private bool _iniciado;

    public ServicoHospedadoObservado(
        IHostedService original,
        PassoDaInicializacao? passo,
        OrdemObservadaDaInicializacao ordem,
        bool descartaOriginal)
    {
        _descartaOriginal = descartaOriginal;
        _original = original;
        _passo = passo;
        _ordem = ordem;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_passo is not { } passo)
        {
            await _original.StartAsync(cancellationToken);
            _iniciado = true;
            return;
        }

        _ordem.Anotar(passo, MomentoDoPasso.Comecou);
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        _ordem.Anotar(passo, MomentoDoPasso.Terminou);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _iniciado ? _original.StopAsync(cancellationToken) : Task.CompletedTask;

    public void Dispose()
    {
        if (_descartaOriginal && _original is IDisposable descartavel)
        {
            descartavel.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_descartaOriginal)
        {
            return;
        }

        if (_original is IAsyncDisposable assincrono)
        {
            await assincrono.DisposeAsync();
        }
        else if (_original is IDisposable descartavel)
        {
            descartavel.Dispose();
        }
    }
}
