namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Text.RegularExpressions;

/// <summary>
/// Reconhecedores das linhas de definição do arquivo SQL: tudo menos o <c>INSERT</c>, lido sem
/// expressão regular por ser a linha que se repete milhões de vezes. Os padrões que citam a tabela
/// trazem os grupos <c>versao</c> e <c>tabela</c>.
/// </summary>
internal static partial class PadroesDoArquivoSqlDne
{
    private const int PrazoEmMilissegundos = 1000;

    [GeneratedRegex(
        """^DROP TABLE IF EXISTS "public"\."tbl_cep_(?<versao>[0-9]{6})_n_(?<tabela>[a-z_]+)";\z""",
        RegexOptions.CultureInvariant,
        PrazoEmMilissegundos)]
    public static partial Regex ApagaTabela();

    [GeneratedRegex(
        """^CREATE TABLE "public"\."tbl_cep_(?<versao>[0-9]{6})_n_(?<tabela>[a-z_]+)" \(\z""",
        RegexOptions.CultureInvariant,
        PrazoEmMilissegundos)]
    public static partial Regex AbreCriacaoDaTabela();

    [GeneratedRegex(
        """^\s*"(?<nome>[a-z_][a-z0-9_]{0,62})" "pg_catalog"\.(?:"(?<varchar>varchar)"(?:\([0-9]+\))?(?: COLLATE "pg_catalog"\."default")?|"(?<inteiro>int4)")(?: NOT NULL)?,?\z""",
        RegexOptions.CultureInvariant,
        PrazoEmMilissegundos)]
    public static partial Regex ColunaDaTabela();

    [GeneratedRegex(
        """^CREATE INDEX "[A-Za-z0-9_]{1,63}" ON "public"\."tbl_cep_(?<versao>[0-9]{6})_n_(?<tabela>[a-z_]+)" USING btree \((?:(?<colunas>.+)\);)?\z""",
        RegexOptions.CultureInvariant,
        PrazoEmMilissegundos)]
    public static partial Regex AbreIndice();

    [GeneratedRegex(
        """^\s*"(?<nome>[a-z_][a-z0-9_]{0,62})"(?: COLLATE "pg_catalog"\."default")?(?: "pg_catalog"\."(?:text_ops|varchar_ops|int4_ops)")?(?: (?:ASC|DESC))?(?: NULLS (?:FIRST|LAST))?,?\z""",
        RegexOptions.CultureInvariant,
        PrazoEmMilissegundos)]
    public static partial Regex ColunaDoIndice();

    [GeneratedRegex(
        """^ALTER TABLE "public"\."tbl_cep_(?<versao>[0-9]{6})_n_(?<tabela>[a-z_]+)" ADD CONSTRAINT "[A-Za-z0-9_]{1,63}" PRIMARY KEY \((?<colunas>"[a-z_][a-z0-9_]{0,62}"(?:, ?"[a-z_][a-z0-9_]{0,62}")*)\);\z""",
        RegexOptions.CultureInvariant,
        PrazoEmMilissegundos)]
    public static partial Regex ChavePrimaria();
}
