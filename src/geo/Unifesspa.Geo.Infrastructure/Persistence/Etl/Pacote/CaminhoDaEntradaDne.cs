namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Buffers;

/// <summary>
/// Separa a pasta e o nome de uma entrada do zip. O pacote tem as entradas no topo ou todas numa
/// pasta de um só nível; a entrada da própria pasta (<c>pasta/</c>) é opcional.
/// </summary>
internal static class CaminhoDaEntradaDne
{
    private const int CaracteresNaPasta = 64;

    private static readonly SearchValues<char> CaracteresDaPasta =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    /// <summary>
    /// Separa o nome completo da entrada. Devolve <see langword="null"/> quando o nome é vazio, longo
    /// demais, tem caractere de controle, <c>\</c>, <c>..</c>, <c>:</c>, começa com <c>/</c>, tem
    /// mais de uma <c>/</c> ou tem pasta fora do padrão. Entrada de pasta (<c>pasta/</c>) tem nome vazio.
    /// </summary>
    public static PartesDaEntrada? Separar(string nomeCompleto)
    {
        if (!NomeEhSeguro(nomeCompleto))
        {
            return null;
        }

        int barra = nomeCompleto.IndexOf('/', StringComparison.Ordinal);
        return barra < 0 ? new PartesDaEntrada(string.Empty, nomeCompleto, EhPasta: false) : SepararPasta(nomeCompleto, barra);
    }

    private static PartesDaEntrada? SepararPasta(string nomeCompleto, int barra)
    {
        if (nomeCompleto.IndexOf('/', barra + 1) >= 0 || !PastaEhValida(nomeCompleto.AsSpan(0, barra)))
        {
            return null;
        }

        string nome = nomeCompleto[(barra + 1)..];
        return new PartesDaEntrada(nomeCompleto[..barra], nome, EhPasta: nome.Length == 0);
    }

    private static bool NomeEhSeguro(string nome) =>
        nome.Length is > 0 and <= LimitesDoPacoteDne.CaracteresNoNomeDaEntrada
        && nome[0] != '/'
        && !nome.Contains('\\', StringComparison.Ordinal)
        && !nome.Contains(':', StringComparison.Ordinal)
        && !nome.Contains("..", StringComparison.Ordinal)
        && !nome.Any(char.IsControl);

    private static bool PastaEhValida(ReadOnlySpan<char> pasta) =>
        pasta.Length is > 0 and <= CaracteresNaPasta
        && pasta.IndexOfAnyExcept(CaracteresDaPasta) < 0;

    /// <summary>Pasta (vazia no topo), nome do arquivo (vazio na entrada da própria pasta) e se é a entrada da pasta.</summary>
    internal sealed record PartesDaEntrada(string Pasta, string Nome, bool EhPasta);
}
