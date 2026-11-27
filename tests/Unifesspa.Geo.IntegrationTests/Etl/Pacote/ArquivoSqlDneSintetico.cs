namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Text;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Monta, em memória, arquivos SQL no formato do pacote dos Correios com valores fictícios.</summary>
internal static class ArquivoSqlDneSintetico
{
    public const string Tabela = "cidade_faixa";
    public const string Versao = "202601";

    public static List<string> ComFechamentoEmUmaLinha(string tabela = Tabela, string versao = Versao) =>
        Criacao(tabela, versao, [");"]);

    public static List<string> ComFechamentoEmDuasLinhas(string tabela = Tabela, string versao = Versao) =>
        Criacao(tabela, versao, [")", ";"]);

    public static string ValoresCompativeis(string tabela = Tabela) =>
        string.Join(", ", TabelasDne.ColunasObrigatorias(tabela).Select(c => c.Tipo == TipoDeColunaDne.Texto ? "'x'" : "1"));

    public static string Insert(string valores, string tabela = Tabela, string versao = Versao) =>
        $"INSERT INTO \"public\".\"{Nome(tabela, versao)}\" VALUES ({valores});";

    public static MemoryStream Fluxo(IEnumerable<string> linhas, string fimDeLinha = "\r\n") =>
        new(new UTF8Encoding(false).GetBytes(string.Join(fimDeLinha, linhas) + fimDeLinha));

    public static string Coluna(string nome, TipoDeColunaDne tipo) => tipo == TipoDeColunaDne.Texto
        ? $"  \"{nome}\" \"pg_catalog\".\"varchar\"(255) COLLATE \"pg_catalog\".\"default\","
        : $"  \"{nome}\" \"pg_catalog\".\"int4\",";

    public static string Nome(string tabela, string versao) => $"tbl_cep_{versao}_n_{tabela}";

    private static List<string> Criacao(string tabela, string versao, string[] fechamento)
    {
        string nome = Nome(tabela, versao);
        List<string> linhas = [$"DROP TABLE IF EXISTS \"public\".\"{nome}\";", $"CREATE TABLE \"public\".\"{nome}\" ("];
        linhas.AddRange(TabelasDne.ColunasObrigatorias(tabela).Select(c => Coluna(c.Nome, c.Tipo)));
        linhas.AddRange(fechamento);
        return linhas;
    }
}
