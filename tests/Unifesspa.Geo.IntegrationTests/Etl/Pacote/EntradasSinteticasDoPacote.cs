namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Entradas fictícias do diretório de um zip do pacote, para a conferência.</summary>
internal static class EntradasSinteticasDoPacote
{
    public const string PastaDoPacote = "pasta_do_pacote";

    public static List<EntradaDoPacoteDne> Completas(string versao = ArquivoSqlDneSintetico.Versao) =>
        [.. TabelasDne.Nomes.Select(tabela => Sql(tabela, versao))];

    /// <summary>Formato do pacote de 202610: entrada da pasta, imagem png e as 15 tabelas dentro da pasta.</summary>
    public static List<EntradaDoPacoteDne> CompletasNaPasta(string pasta = PastaDoPacote, string versao = ArquivoSqlDneSintetico.Versao) =>
    [
        Pasta(pasta),
        new($"{pasta}/0relacionamentos.png", 2048, Cifrada: false),
        .. Completas(versao).Select(e => e with { Nome = $"{pasta}/{e.Nome}" }),
    ];

    public static EntradaDoPacoteDne Sql(string tabela, string versao, long tamanho = 1024) =>
        new(NomeDaTabelaDne.Entrada(versao, tabela), tamanho, Cifrada: false);

    public static EntradaDoPacoteDne Pasta(string pasta, long tamanho = 0) =>
        new($"{pasta}/", tamanho, Cifrada: false);

    public static EntradaDoPacoteDne Imagem(string nome = "0relacionamentos.jpg", long tamanho = 2048) =>
        new(nome, tamanho, Cifrada: false);
}
