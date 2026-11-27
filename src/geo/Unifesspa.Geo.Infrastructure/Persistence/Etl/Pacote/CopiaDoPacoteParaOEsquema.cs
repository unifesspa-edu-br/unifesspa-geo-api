namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.IO.Compression;

using Npgsql;

using NpgsqlTypes;

/// <summary>
/// Lê o zip em fluxo, uma tabela por vez, e carrega cada uma no esquema de carga: cria a tabela
/// com as colunas validadas pela leitura e manda as linhas por COPY binário. O conteúdo descompactado
/// é contado contra os limites por entrada e no total.
/// </summary>
internal static class CopiaDoPacoteParaOEsquema
{
    public static async Task CopiarAsync(
        NpgsqlConnection conexao,
        string esquema,
        string caminhoDoZip,
        string versao,
        CancellationToken cancellationToken)
    {
        using ZipArchive zip = ArquivoDoPacoteDne.AbrirZip(caminhoDoZip);
        long bytesDescompactados = 0;
        foreach (string tabela in TabelasDne.Nomes)
        {
            bytesDescompactados += await CopiarTabelaAsync(conexao, esquema, zip, (versao, tabela), bytesDescompactados, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<long> CopiarTabelaAsync(
        NpgsqlConnection conexao,
        string esquema,
        ZipArchive zip,
        (string Versao, string Tabela) alvo,
        long bytesJaDescompactados,
        CancellationToken cancellationToken)
    {
        Stream fluxo = await ArquivoDoPacoteDne.AbrirEntradaAsync(zip, alvo.Versao, alvo.Tabela, cancellationToken).ConfigureAwait(false);
        await using (fluxo.ConfigureAwait(false))
        {
            LeitorDoArquivoSqlDne leitor = new(
                fluxo, alvo.Tabela, alvo.Versao, LimitesDoPacoteDne.BytesRestantesParaEntrada(bytesJaDescompactados));
            IReadOnlyList<ColunaDne> colunas = await leitor.LerCabecalhoAsync(cancellationToken).ConfigureAwait(false);

            await ExecutorDeComandosDoEsquema.ExecutarAsync(conexao, ComandosDoEsquemaDeCarga.CriarTabela(esquema, alvo.Tabela), cancellationToken).ConfigureAwait(false);
            await CopiarLinhasAsync(conexao, ComandosDoEsquemaDeCarga.Copiar(esquema, alvo.Tabela), colunas, leitor, cancellationToken)
                .ConfigureAwait(false);
            return leitor.BytesDescompactados;
        }
    }

    private static async Task CopiarLinhasAsync(
        NpgsqlConnection conexao,
        ComandoDoEsquemaDeCarga comandoCopiar,
        IReadOnlyList<ColunaDne> colunas,
        LeitorDoArquivoSqlDne leitor,
        CancellationToken cancellationToken)
    {
        NpgsqlBinaryImporter importador = await conexao.BeginBinaryImportAsync(comandoCopiar.Texto, cancellationToken).ConfigureAwait(false);
        await using (importador.ConfigureAwait(false))
        {
            await foreach (object?[] valores in leitor.LerLinhasAsync(cancellationToken).ConfigureAwait(false))
            {
                await EscreverLinhaAsync(importador, colunas, valores, cancellationToken).ConfigureAwait(false);
            }

            await importador.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EscreverLinhaAsync(
        NpgsqlBinaryImporter importador,
        IReadOnlyList<ColunaDne> colunas,
        object?[] valores,
        CancellationToken cancellationToken)
    {
        await importador.StartRowAsync(cancellationToken).ConfigureAwait(false);
        for (int i = 0; i < valores.Length; i++)
        {
            await EscreverValorAsync(importador, colunas[i].Tipo, valores[i], cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task EscreverValorAsync(NpgsqlBinaryImporter importador, TipoDeColunaDne tipo, object? valor, CancellationToken cancellationToken) =>
        valor switch
        {
            null => importador.WriteNullAsync(cancellationToken),
            int inteiro when tipo == TipoDeColunaDne.Inteiro => importador.WriteAsync(inteiro, NpgsqlDbType.Integer, cancellationToken),
            string texto when tipo == TipoDeColunaDne.Texto => importador.WriteAsync(texto, NpgsqlDbType.Text, cancellationToken),
            _ => throw new InvalidOperationException("Valor incompatível com o tipo da coluna."),
        };
}
