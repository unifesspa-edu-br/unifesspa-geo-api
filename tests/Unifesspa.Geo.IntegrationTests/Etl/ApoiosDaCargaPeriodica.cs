namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Infrastructure.Core.Caching;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

internal sealed class MemoriaCacheFake : ICacheService
{
    private readonly Dictionary<string, object?> _dados = [];

    public Task<T?> ObterAsync<T>(string chave, CancellationToken cancellationToken = default) =>
        Task.FromResult(_dados.TryGetValue(chave, out object? valor) ? (T?)valor : default);

    public Task DefinirAsync<T>(string chave, T valor, TimeSpan? expiracao = null, CancellationToken cancellationToken = default)
    {
        _dados[chave] = valor;
        return Task.CompletedTask;
    }

    public Task RemoverAsync(string chave, CancellationToken cancellationToken = default)
    {
        _dados.Remove(chave);
        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable?> AcquireLeaseAsync(string chave, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable?>(null);
}

internal sealed class FonteFactoryFake : IFonteDaExecucaoDne
{
    private readonly IReadOnlyDictionary<string, IFonteDaCargaDne> _porVersao;

    public FonteFactoryFake(IReadOnlyDictionary<string, IFonteDaCargaDne> porVersao)
    {
        _porVersao = porVersao;
    }

    public Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken) =>
        Task.FromResult(_porVersao[versao]);

    public Task DescartarPacoteAsync(Guid execucaoId) => Task.CompletedTask;

    public Task<int> DescartarSobrasAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}

// Simula cancelamento (desligamento) no meio da carga: o executor da carga deve marcar a
// execução como terminal e repropagar o OperationCanceledException.
internal sealed class FonteFactoryCancelada : IFonteDaExecucaoDne
{
    public Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken) =>
        throw new OperationCanceledException();

    public Task DescartarPacoteAsync(Guid execucaoId) => Task.CompletedTask;

    public Task<int> DescartarSobrasAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
