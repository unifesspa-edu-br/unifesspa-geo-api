namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Executa o leitor sobre um fluxo e junta o cabeçalho e as linhas lidas.</summary>
internal static class LeituraDoArquivoSqlDne
{
    public static async Task<List<object?[]>> LerTudoAsync(
        Stream fluxo,
        string tabela = ArquivoSqlDneSintetico.Tabela,
        long limiteDeBytes = LimitesDoPacoteDne.BytesPorEntrada,
        string versao = ArquivoSqlDneSintetico.Versao)
    {
        LeitorDoArquivoSqlDne leitor = new(fluxo, tabela, versao, limiteDeBytes);
        await leitor.LerCabecalhoAsync(CancellationToken.None);
        List<object?[]> linhas = [];
        await foreach (object?[] valores in leitor.LerLinhasAsync(CancellationToken.None))
        {
            linhas.Add(valores);
        }

        return linhas;
    }

    public static Task<List<object?[]>> LerTudoAsync(IEnumerable<string> linhas, string fimDeLinha = "\r\n") =>
        LerTudoAsync(ArquivoSqlDneSintetico.Fluxo(linhas, fimDeLinha));
}
