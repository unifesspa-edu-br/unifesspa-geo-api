namespace Unifesspa.Geo.IntegrationTests.Etl;

/// <summary>Corpo em memória que executa um gancho do teste na primeira leitura, antes de entregar os bytes.</summary>
internal sealed class FluxoComGancho : MemoryStream
{
    private Func<Task>? _aoLer;

    public FluxoComGancho(byte[] bytes, Func<Task> aoLer)
        : base(bytes)
    {
        _aoLer = aoLer;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Func<Task>? gancho = Interlocked.Exchange(ref _aoLer, null);
        if (gancho is not null)
        {
            await gancho();
        }

        return await base.ReadAsync(buffer, cancellationToken);
    }
}
