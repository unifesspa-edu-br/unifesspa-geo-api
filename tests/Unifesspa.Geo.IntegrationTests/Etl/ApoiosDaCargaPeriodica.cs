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

internal sealed class FonteFactoryFake : IGeoFonteDadosFactory
{
    private readonly IReadOnlyDictionary<string, IGeoFonteDados> _porVersao;

    public FonteFactoryFake(IReadOnlyDictionary<string, IGeoFonteDados> porVersao)
    {
        _porVersao = porVersao;
    }

    public IGeoFonteDados Criar(string versao) => _porVersao[versao];
}

// Simula cancelamento (desligamento) no meio da carga: o executor da carga deve marcar a
// execução como terminal e repropagar o OperationCanceledException.
internal sealed class FonteFactoryCancelada : IGeoFonteDadosFactory
{
    public IGeoFonteDados Criar(string versao) => throw new OperationCanceledException();
}
