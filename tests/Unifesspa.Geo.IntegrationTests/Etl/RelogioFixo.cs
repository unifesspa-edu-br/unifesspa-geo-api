namespace Unifesspa.Geo.IntegrationTests.Etl;

/// <summary>
/// Relógio de teste que só muda quando o teste manda: a carga compara os carimbos gravados
/// com o instante em que iniciou, então cada recarga precisa começar depois da anterior.
/// </summary>
internal sealed class RelogioFixo : TimeProvider
{
    private DateTimeOffset _agora = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan prazo) => _agora += prazo;
}
