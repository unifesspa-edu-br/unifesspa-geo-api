namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Entradas fictícias do diretório de um zip do pacote, para a conferência.</summary>
internal static class EntradasSinteticasDoPacote
{
    public static List<EntradaDoPacoteDne> Completas(string versao = ArquivoSqlDneSintetico.Versao) =>
        [.. TabelasDne.Nomes.Select(tabela => Sql(tabela, versao))];

    public static EntradaDoPacoteDne Sql(string tabela, string versao, long tamanho = 1024) =>
        new(NomeDaTabelaDne.Entrada(versao, tabela), tamanho, Cifrada: false);

    public static EntradaDoPacoteDne Imagem() =>
        new("0relacionamentos.jpg", 2048, Cifrada: false);
}
