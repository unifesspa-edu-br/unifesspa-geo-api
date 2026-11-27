namespace Unifesspa.Geo.Infrastructure.Core.Formatting;

using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Net.Http.Headers;

/// <summary>
/// Escolhe, entre as versões que o recurso oferece, a que o cabeçalho <c>Accept</c> aceita
/// (ADR-0028). <c>application/json</c>, <c>application/*</c> e <c>*/*</c> aceitam a versão mais
/// recente, a última da lista; <c>application/vnd.uniplus.&lt;recurso&gt;.v&lt;N&gt;+json</c> aceita
/// a versão <c>N</c>.
/// </summary>
internal sealed partial class NegociacaoDaVersaoDeMidia
{
    internal const string PrefixoDoFornecedor = "application/vnd.uniplus.";
    internal const string SufixoJson = "+json";

    private readonly string _recurso;
    private readonly int[] _versoes;

    public NegociacaoDaVersaoDeMidia(string recurso, int[] versoes)
    {
        _recurso = recurso;
        _versoes = versoes;
    }

    /// <summary>
    /// A versão aceita, ou <c>null</c> quando nenhum dos tipos pedidos serve. O maior valor de
    /// qualidade vence (RFC 9110 §12.5.1) e a ordenação estável mantém a ordem do cabeçalho no
    /// empate; qualidade ausente vale 1,0.
    /// </summary>
    public int? VersaoAceita(IList<MediaTypeHeaderValue> pedidos)
    {
        MediaTypeHeaderValue[] ordenados = [.. pedidos.OrderByDescending(static pedido => pedido.Quality ?? 1.0)];
        ExclusoesDoAccept exclusoes = Exclusoes(ordenados);

        return ordenados
            .Where(static pedido => pedido.Quality is not 0)
            .Select(pedido => VersaoDoPedido(pedido, exclusoes))
            .FirstOrDefault(static versao => versao is not null);
    }

    // RFC 9110 §12.5.1: q=0 não é só "ignorar", é "excluir do match". As exclusões são lidas antes
    // de qualquer aceite, para valerem também sobre o curinga e sobre a versão pedida pelo nome.
    private ExclusoesDoAccept Exclusoes(IEnumerable<MediaTypeHeaderValue> ordenados)
    {
        HashSet<int> versoes = [];
        bool todasPeloCuringa = false;

        foreach (MediaTypeHeaderValue excluido in ordenados.Where(static pedido => pedido.Quality is 0))
        {
            string tipo = excluido.MediaType.Value ?? string.Empty;
            if (EhCuringaOuJson(tipo))
                todasPeloCuringa = true;
            else if (TentarLerVersaoDoFornecedor(tipo, out int versao))
                versoes.Add(versao);
        }

        return new ExclusoesDoAccept(todasPeloCuringa, versoes);
    }

    private int? VersaoDoPedido(MediaTypeHeaderValue pedido, ExclusoesDoAccept exclusoes)
    {
        string tipo = pedido.MediaType.Value ?? string.Empty;
        int maisRecente = _versoes[^1];

        if (EhCuringaOuJson(tipo))
            return exclusoes.ExcluiACuringa(maisRecente) ? null : maisRecente;

        return TentarLerVersaoDoFornecedor(tipo, out int versao)
            && Array.IndexOf(_versoes, versao) >= 0
            && !exclusoes.ExcluiAVersao(versao)
                ? versao
                : null;
    }

    private bool TentarLerVersaoDoFornecedor(string tipo, out int versao)
    {
        versao = 0;

        // RFC 9110 §8.3.1: type/subtype são case-insensitive.
        if (!tipo.StartsWith(PrefixoDoFornecedor, StringComparison.OrdinalIgnoreCase)
            || !tipo.EndsWith(SufixoJson, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Match encontrado = RecursoEVersao().Match(tipo[PrefixoDoFornecedor.Length..^SufixoJson.Length]);
        return encontrado.Success
            && string.Equals(encontrado.Groups["resource"].Value, _recurso, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(encontrado.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out versao);
    }

    // RFC 9110 §8.3.1: os tokens de tipo de mídia são case-insensitive.
    private static bool EhCuringaOuJson(string tipo) =>
        string.Equals(tipo, "*/*", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tipo, "application/*", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tipo, "application/json", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<resource>[a-z][a-z0-9_-]*)\.v(?<version>\d+)$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecursoEVersao();

    private readonly record struct ExclusoesDoAccept(bool TodasPeloCuringa, HashSet<int> Versoes)
    {
        public bool ExcluiACuringa(int maisRecente) => TodasPeloCuringa || Versoes.Contains(maisRecente);

        // A exclusão pelo curinga não barra uma versão pedida pelo nome; só a exclusão da própria
        // versão barra.
        public bool ExcluiAVersao(int versao) => Versoes.Contains(versao);
    }
}
