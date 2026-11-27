namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Prazo máximo de uma carga: cancela a execução ao esgotar a duração configurada e
/// informa se o cancelamento veio do prazo ou de quem pediu para interromper.
/// </summary>
internal sealed class PrazoDaCarga : IDisposable
{
    private readonly CancellationTokenSource _origemDoPrazo;
    private readonly CancellationTokenSource _origemLigada;
    private readonly CancellationToken _cancelamentoExterno;

    public PrazoDaCarga(TimeSpan duracaoMaxima, TimeProvider relogio, CancellationToken cancelamentoExterno)
    {
        ArgumentNullException.ThrowIfNull(relogio);

        _cancelamentoExterno = cancelamentoExterno;
        _origemDoPrazo = new CancellationTokenSource(duracaoMaxima, relogio);
        _origemLigada = CancellationTokenSource.CreateLinkedTokenSource(cancelamentoExterno, _origemDoPrazo.Token);
    }

    /// <summary>Token que cancela por prazo esgotado ou por pedido externo.</summary>
    public CancellationToken Token => _origemLigada.Token;

    /// <summary>Verdadeiro quando o prazo esgotou e não houve pedido externo de cancelamento.</summary>
    public bool FoiExcedido => _origemDoPrazo.IsCancellationRequested && !_cancelamentoExterno.IsCancellationRequested;

    public void Dispose()
    {
        _origemLigada.Dispose();
        _origemDoPrazo.Dispose();
    }
}
