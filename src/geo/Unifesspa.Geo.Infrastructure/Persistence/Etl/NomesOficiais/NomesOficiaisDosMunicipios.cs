namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.NomesOficiais;

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Nome oficial de cada município, na grafia do IBGE (por exemplo «Pau D'Arco»), indexado
/// pelo código IBGE de 7 dígitos. Vem da lista embutida no assembly
/// (<c>municipios-ibge.tsv</c>), lida uma única vez; a DNE traz o mesmo município com
/// outra grafia em parte dos casos, e o nome oficial é o que vale no Uni+.
/// </summary>
internal sealed class NomesOficiaisDosMunicipios
{
    private const string NomeDoRecurso = "Unifesspa.Geo.Infrastructure.Persistence.Etl.NomesOficiais.municipios-ibge.tsv";
    private const string Cabecalho = "codigo_ibge\tuf\tnome";
    private const int TamanhoDoCodigoIbge = 7;
    private const int ColunasPorLinha = 3;

    private readonly FrozenDictionary<string, string> _nomesPorCodigo;

    private NomesOficiaisDosMunicipios(FrozenDictionary<string, string> nomesPorCodigo)
    {
        _nomesPorCodigo = nomesPorCodigo;
    }

    /// <summary>Lista oficial embutida no assembly, carregada uma vez.</summary>
    public static NomesOficiaisDosMunicipios Padrao { get; } = LerDoAssembly();

    public int Quantidade => _nomesPorCodigo.Count;

    /// <summary>
    /// Lê uma lista no formato do arquivo embutido: cabeçalho <c>codigo_ibge</c>, <c>uf</c>,
    /// <c>nome</c> separados por tabulação e uma linha por município. Recusa o arquivo
    /// inteiro se o cabeçalho, o código ou o nome de alguma linha estiver errado.
    /// </summary>
    public static NomesOficiaisDosMunicipios Ler(TextReader leitor)
    {
        ArgumentNullException.ThrowIfNull(leitor);

        ExigirCabecalho(leitor.ReadLine());
        return new NomesOficiaisDosMunicipios(LerLinhas(leitor).ToFrozenDictionary(StringComparer.Ordinal));
    }

    public bool TentarObter(string codigoIbge, [NotNullWhen(true)] out string? nome)
    {
        ArgumentNullException.ThrowIfNull(codigoIbge);
        return _nomesPorCodigo.TryGetValue(codigoIbge, out nome);
    }

    private static NomesOficiaisDosMunicipios LerDoAssembly()
    {
        using Stream recurso = typeof(NomesOficiaisDosMunicipios).Assembly.GetManifestResourceStream(NomeDoRecurso)
            ?? throw new InvalidOperationException($"O recurso embutido '{NomeDoRecurso}' não foi encontrado no assembly.");
        using StreamReader leitor = new(recurso, System.Text.Encoding.UTF8);
        return Ler(leitor);
    }

    private static Dictionary<string, string> LerLinhas(TextReader leitor)
    {
        Dictionary<string, string> nomes = new(StringComparer.Ordinal);
        int numeroDaLinha = 1;
        while (leitor.ReadLine() is { } linha)
        {
            numeroDaLinha++;
            AdicionarLinha(nomes, linha, numeroDaLinha);
        }

        return nomes;
    }

    private static void ExigirCabecalho(string? linha)
    {
        if (!string.Equals(linha, Cabecalho, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"A lista de municípios do IBGE deve começar pelo cabeçalho '{Cabecalho}'.");
        }
    }

    private static void AdicionarLinha(Dictionary<string, string> nomes, string linha, int numeroDaLinha)
    {
        string[] colunas = linha.Split('\t');
        ExigirLinhaValida(colunas, numeroDaLinha);

        if (!nomes.TryAdd(colunas[0], colunas[2]))
        {
            throw new InvalidDataException($"Linha {numeroDaLinha} da lista de municípios do IBGE repete o código {colunas[0]}.");
        }
    }

    private static void ExigirLinhaValida(string[] colunas, int numeroDaLinha)
    {
        ExigirTresColunas(colunas, numeroDaLinha);
        ExigirCodigoDeSeteDigitos(colunas[0], numeroDaLinha);
        ExigirNome(colunas[2], numeroDaLinha);
    }

    private static void ExigirTresColunas(string[] colunas, int numeroDaLinha)
    {
        if (colunas.Length != ColunasPorLinha)
        {
            throw new InvalidDataException($"Linha {numeroDaLinha} da lista de municípios do IBGE deve ter {ColunasPorLinha} colunas.");
        }
    }

    private static void ExigirCodigoDeSeteDigitos(string codigo, int numeroDaLinha)
    {
        if (codigo.Length != TamanhoDoCodigoIbge || !codigo.All(char.IsAsciiDigit))
        {
            throw new InvalidDataException($"Linha {numeroDaLinha} da lista de municípios do IBGE tem código sem {TamanhoDoCodigoIbge} dígitos.");
        }
    }

    private static void ExigirNome(string nome, int numeroDaLinha)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new InvalidDataException($"Linha {numeroDaLinha} da lista de municípios do IBGE tem nome vazio.");
        }
    }
}
