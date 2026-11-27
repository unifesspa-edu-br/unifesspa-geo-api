namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>Entrega a fonte em memória cadastrada para cada versão pedida; não há pacote nem sobras a limpar.</summary>
internal sealed class FabricaDeFonteFixa : IFonteDaExecucaoDne
{
    private readonly Dictionary<string, IFonteDaCargaDne> _porVersao;

    public FabricaDeFonteFixa(params IFonteDaCargaDne[] fontes)
    {
        ArgumentNullException.ThrowIfNull(fontes);

        _porVersao = fontes.ToDictionary(f => f.Versao, StringComparer.Ordinal);
    }

    public Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken) =>
        Task.FromResult(_porVersao[versao]);

    public Task DescartarPacoteAsync(Guid execucaoId) => Task.CompletedTask;

    public Task<int> DescartarSobrasAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
