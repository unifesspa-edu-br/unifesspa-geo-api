namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Carga já registrada como <c>EmAndamento</c> que mantém a trava consultiva da carga DNE
/// desde o pedido até o fim da execução. Liberar mais de uma vez é inofensivo: só a primeira
/// libera a trava.
/// </summary>
internal sealed class CargaReservada : IAsyncDisposable
{
    private IAsyncDisposable? _travaDaCarga;

    private CargaReservada(Guid execucaoId, IAsyncDisposable travaDaCarga)
    {
        ArgumentNullException.ThrowIfNull(travaDaCarga);

        ExecucaoId = execucaoId;
        _travaDaCarga = travaDaCarga;
    }

    public Guid ExecucaoId { get; }

    /// <summary>Indica que a fila já recebeu a carga: dali em diante, só o consumidor solta a trava.</summary>
    public bool EntregueAFila { get; private set; }

    public void MarcarEntregueAFila() => EntregueAFila = true;

    /// <summary>Entrega a trava obtida no pedido à execução <paramref name="execucaoId"/>.</summary>
    public static CargaReservada Reservar(Guid execucaoId, IAsyncDisposable travaDaCarga) =>
        new(execucaoId, travaDaCarga);

    public ValueTask DisposeAsync()
    {
        IAsyncDisposable? trava = Interlocked.Exchange(ref _travaDaCarga, null);
        return trava is null ? ValueTask.CompletedTask : trava.DisposeAsync();
    }
}
