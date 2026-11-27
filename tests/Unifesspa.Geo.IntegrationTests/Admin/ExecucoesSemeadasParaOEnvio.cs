namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Security.Cryptography;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Execuções anteriores gravadas direto no banco, para preparar o cenário do envio.</summary>
internal sealed class ExecucoesSemeadasParaOEnvio
{
    private readonly GeoPostgisFixture _fixture;

    public ExecucoesSemeadasParaOEnvio(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public static string ResumoDe(byte[] zip) => Convert.ToHexStringLower(SHA256.HashData(zip));

    /// <summary>Carga concluída do pacote informado; devolve o identificador da execução.</summary>
    public async Task<Guid> ConcluidaAsync(string versao, byte[] zip)
    {
        GeoImportacaoExecucao execucao = Nova(versao, zip);
        execucao.Concluir(TimeProvider.System.GetUtcNow(), "{}", "ok");
        await GravarAsync(execucao);
        return execucao.Id;
    }

    /// <summary>Carga ainda em andamento, que segura o índice único de execuções em andamento.</summary>
    public async Task EmAndamentoAsync(string versao, byte[] zip) => await GravarAsync(Nova(versao, zip));

    private static GeoImportacaoExecucao Nova(string versao, byte[] zip) =>
        GeoImportacaoExecucao
            .Iniciar(new PacoteRecebido(versao, ResumoDe(zip), zip.Length), "teste", TimeProvider.System.GetUtcNow())
            .Value!;

    private async Task GravarAsync(GeoImportacaoExecucao execucao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        contexto.ImportacaoExecucoes.Add(execucao);
        await contexto.SaveChangesAsync();
    }
}
