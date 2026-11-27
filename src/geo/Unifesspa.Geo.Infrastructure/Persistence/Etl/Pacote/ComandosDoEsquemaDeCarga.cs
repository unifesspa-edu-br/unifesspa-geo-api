namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Buffers;
using System.Globalization;
using System.Text;

/// <summary>
/// Único lugar que escreve SQL do esquema de carga, e o único registro da regra de segurança:
/// nenhum nome vindo do arquivo do pacote chega a um comando. O esquema é o nome produzido para a
/// execução (<see cref="Nome"/>); as tabelas são as 15 de <see cref="TabelasDne"/>; as colunas são
/// só as obrigatórias de <see cref="TabelasDne.ColunasObrigatorias"/>, com tipos da lista fechada
/// (<c>varchar</c> vira <c>text</c> e <c>int4</c> vira <c>integer</c>). Todo nome é conferido ao
/// montar o comando, que sai como <see cref="ComandoDoEsquemaDeCarga"/>. Os valores do arquivo
/// nunca entram em SQL: seguem pelo COPY binário.
/// </summary>
internal static class ComandosDoEsquemaDeCarga
{
    public const string Prefixo = "carga_dne_";

    // CEP geral de cidade ou distrito: linha de logradouro sem tipo, sem nome e sem texto completo.
    private const string SemNomeDeLogradouro =
        "btrim(coalesce(tipo, '')) = '' AND btrim(coalesce(nome_logradouro, '')) = '' AND btrim(coalesce(logradouro, '')) = ''";

    private const int DigitosDoIdentificador = 32;
    private const int TamanhoMaximoDoIdentificador = 63;

    private static readonly SearchValues<char> DigitosHexadecimais = SearchValues.Create("0123456789abcdef");
    private static readonly SearchValues<char> CaracteresDeIdentificador =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789_");

    /// <summary>Nome do esquema da execução: <c>carga_dne_</c> seguido do identificador sem hífens.</summary>
    public static string Nome(Guid execucaoId) =>
        Prefixo + execucaoId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>O texto tem exatamente a forma de um nome produzido por <see cref="Nome"/>.</summary>
    public static bool EhNomeValido(string nome) =>
        nome is not null
        && nome.Length == Prefixo.Length + DigitosDoIdentificador
        && nome.StartsWith(Prefixo, StringComparison.Ordinal)
        && nome.AsSpan(Prefixo.Length).IndexOfAnyExcept(DigitosHexadecimais) < 0;

    public static ComandoDoEsquemaDeCarga CriarEsquema(string esquema) => new($"CREATE SCHEMA {Citar(esquema)}");

    public static ComandoDoEsquemaDeCarga DescartarEsquema(string esquema) =>
        new($"DROP SCHEMA IF EXISTS {Citar(esquema)} CASCADE");

    public static ComandoDoEsquemaDeCarga CriarTabela(string esquema, string tabela) =>
        new($"CREATE UNLOGGED TABLE {Qualificar(esquema, tabela)} ({string.Join(", ", ColunasObrigatorias(tabela).Select(DefinirColuna))})");

    public static ComandoDoEsquemaDeCarga Copiar(string esquema, string tabela) =>
        new($"COPY {Qualificar(esquema, tabela)} ({string.Join(", ", ColunasObrigatorias(tabela).Select(c => Citar(c.Nome)))}) FROM STDIN (FORMAT BINARY)");

    /// <summary>
    /// <c>SELECT</c> das colunas pedidas, que precisam estar entre as obrigatórias da tabela, na ordem
    /// informada, com o filtro da linha escolhido entre os fixos de <see cref="FiltroDeLinhasDoEsquema"/>.
    /// </summary>
    public static ComandoDoEsquemaDeCarga Consultar(string esquema, string tabela, FiltroDeLinhasDoEsquema filtro, params string[] colunas)
    {
        IReadOnlyList<ColunaDne> obrigatorias = ColunasObrigatorias(tabela);
        return colunas.All(nome => obrigatorias.Any(c => c.Nome == nome))
            ? new($"SELECT {string.Join(", ", colunas.Select(Citar))} FROM {Qualificar(esquema, tabela)}{Condicao(filtro)}")
            : throw new ArgumentException("Coluna fora das obrigatórias da tabela.", nameof(colunas));
    }

    private static IReadOnlyList<ColunaDne> ColunasObrigatorias(string tabela) => TabelasDne.ColunasObrigatorias(tabela);

    private static string Condicao(FiltroDeLinhasDoEsquema filtro) => filtro switch
    {
        FiltroDeLinhasDoEsquema.Nenhum => string.Empty,
        FiltroDeLinhasDoEsquema.SemNomeDeLogradouro => $" WHERE {SemNomeDeLogradouro}",
        FiltroDeLinhasDoEsquema.ComNomeDeLogradouro => $" WHERE NOT ({SemNomeDeLogradouro})",
        _ => throw new ArgumentOutOfRangeException(nameof(filtro)),
    };

    private static string Qualificar(string esquema, string tabela) =>
        TabelasDne.Contem(tabela)
            ? $"{Citar(esquema)}.{Citar(tabela)}"
            : throw new ArgumentException("Tabela fora da lista do pacote.", nameof(tabela));

    private static string DefinirColuna(ColunaDne coluna) =>
        $"{Citar(coluna.Nome)} {(coluna.Tipo == TipoDeColunaDne.Inteiro ? "integer" : "text")}";

    private static string Citar(string identificador)
    {
        bool valido = !string.IsNullOrEmpty(identificador)
            && identificador.Length <= TamanhoMaximoDoIdentificador
            && !char.IsAsciiDigit(identificador[0])
            && identificador.AsSpan().IndexOfAnyExcept(CaracteresDeIdentificador) < 0;

        return valido
            ? new StringBuilder("\"").Append(identificador).Append('"').ToString()
            : throw new ArgumentException("Identificador fora do padrão aceito no esquema de carga.", nameof(identificador));
    }
}
