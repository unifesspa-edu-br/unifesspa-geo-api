namespace Unifesspa.Geo.IntegrationTests.Paginacao;

/// <summary>
/// Relógio de teste que avança um milissegundo a cada leitura. Um instante calculado uma só vez
/// aparece igual em todo lugar em que é usado; calculado duas vezes, aparece diferente.
/// </summary>
internal sealed class RelogioQueAvancaACadaLeitura : TimeProvider
{
    private long _leituras;

    public RelogioQueAvancaACadaLeitura(DateTimeOffset inicio)
    {
        Inicio = inicio;
    }

    public DateTimeOffset Inicio { get; }

    public override DateTimeOffset GetUtcNow() => Inicio.AddMilliseconds(Interlocked.Increment(ref _leituras));
}
