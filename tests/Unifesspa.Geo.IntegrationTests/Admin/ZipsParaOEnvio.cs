namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.IO.Compression;
using System.Text;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Etl.Pacote;

/// <summary>
/// Conteúdos fictícios para o envio do pacote. O marcador sigiloso fictício vai dentro do arquivo
/// para provar que o conteúdo nunca aparece em resposta nem em registro de log.
/// </summary>
internal static class ZipsParaOEnvio
{
    public const string MarcadorSigiloso = "SEGREDO-FICTICIO-7391";

    public static byte[] Valido(string versao) => PacoteSinteticoDne.Completo(versao).MontarZip();

    public static byte[] ComVersaoMisturada() =>
        Montar(TabelasDne.Nomes.Select((tabela, indice) => NomeDaTabelaDne.Entrada(indice == 0 ? "202611" : "202610", tabela)));

    public static byte[] SemUmaTabela() =>
        Montar(TabelasDne.Nomes.Skip(1).Select(tabela => NomeDaTabelaDne.Entrada("202610", tabela)));

    public static byte[] NaoEhZip() => Encoding.UTF8.GetBytes($"texto que não é um zip {MarcadorSigiloso}");

    public static byte[] AcimaDe(int bytes) => new byte[bytes + 1];

    private static byte[] Montar(IEnumerable<string> nomes)
    {
        using MemoryStream saida = new();
        using (ZipArchive zip = new(saida, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string nome in nomes)
            {
                using StreamWriter entrada = new(zip.CreateEntry(nome).Open(), Encoding.UTF8);
                entrada.Write(MarcadorSigiloso);
            }
        }

        return saida.ToArray();
    }
}
