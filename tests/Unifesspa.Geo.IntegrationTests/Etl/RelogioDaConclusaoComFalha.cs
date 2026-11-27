namespace Unifesspa.Geo.IntegrationTests.Etl;

/// <summary>
/// Relógio que falha numa chamada escolhida pelo teste, depois de armado. Antes de falhar,
/// executa o gancho do teste, que observa o banco dentro da transação da carga.
/// </summary>
internal sealed class RelogioDaConclusaoComFalha : TimeProvider
{
    private readonly TimeProvider _relogio;
    private readonly Action _antesDeFalhar;
    private int _chamadasAteFalhar;

    public RelogioDaConclusaoComFalha(TimeProvider relogio, Action antesDeFalhar)
    {
        _relogio = relogio;
        _antesDeFalhar = antesDeFalhar;
    }

    /// <summary>Deixa passar o número informado de chamadas e falha na seguinte, uma única vez.</summary>
    public void FalharAposChamadas(int chamadasQuePassam) => _chamadasAteFalhar = chamadasQuePassam + 1;

    public override DateTimeOffset GetUtcNow()
    {
        if (_chamadasAteFalhar > 0 && --_chamadasAteFalhar == 0)
        {
            _antesDeFalhar();
            throw new InvalidOperationException("Falha simulada ao carimbar a conclusão da carga.");
        }

        return _relogio.GetUtcNow();
    }
}
