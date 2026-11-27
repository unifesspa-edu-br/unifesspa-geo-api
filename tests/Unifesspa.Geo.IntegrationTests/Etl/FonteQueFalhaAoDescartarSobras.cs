namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>Fonte cujo descarte dos esquemas que sobraram lança a exceção informada.</summary>
internal sealed class FonteQueFalhaAoDescartarSobras : IFonteDaExecucaoDne
{
    private readonly Exception _excecao;

    public FonteQueFalhaAoDescartarSobras(Exception excecao)
    {
        _excecao = excecao;
    }

    public Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Esta fonte não prepara cargas.");

    public Task DescartarPacoteAsync(Guid execucaoId) => Task.CompletedTask;

    public Task<int> DescartarSobrasAsync(CancellationToken cancellationToken) => Task.FromException<int>(_excecao);
}
