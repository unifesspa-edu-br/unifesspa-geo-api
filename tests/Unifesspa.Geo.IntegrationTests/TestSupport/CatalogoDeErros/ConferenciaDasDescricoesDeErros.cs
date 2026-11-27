namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

/// <summary>
/// Confere as descrições públicas do Geo contra o catálogo de códigos. Só os códigos
/// <c>uniplus.geo.*</c> são descritos aqui; os comuns são descritos pela API.
/// </summary>
internal static class ConferenciaDasDescricoesDeErros
{
    internal const string PrefixoDoGeo = "uniplus.geo.";

    internal static string[] Conferir(DocumentoDeDescricoesDeErros documento, IEnumerable<string> publicados)
    {
        DescricaoDeErro?[] descricoes = [.. documento.Descricoes ?? []];
        HashSet<string> esperados = [.. publicados.Where(static c => c.StartsWith(PrefixoDoGeo, StringComparison.Ordinal))];
        string[] codigos = [.. descricoes.Select(static descricao => CodigoSeguro(descricao?.Codigo))];
        return
        [
            .. ConferirDocumento(documento),
            .. ConferirPares(esperados, codigos),
            .. descricoes.SelectMany(ConferirDescricao).Distinct(StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<string> ConferirDocumento(DocumentoDeDescricoesDeErros documento)
    {
        if (documento.VersaoDoFormato != DocumentoDeDescricoesDeErros.VersaoAtual)
        {
            yield return "descricoes-de-erros.json: versão do formato desconhecida.";
        }

        if (documento.Descricoes is null)
        {
            yield return "descricoes-de-erros.json: falta a lista de descrições.";
        }
    }

    private static IEnumerable<string> ConferirPares(HashSet<string> esperados, string[] descritos)
    {
        foreach (string codigo in esperados.Except(descritos).Order(StringComparer.Ordinal))
        {
            yield return $"{codigo}: código sem descrição.";
        }

        foreach (string codigo in descritos.Except(esperados).Where(static c => c.Length > 0).Order(StringComparer.Ordinal))
        {
            yield return codigo.StartsWith(PrefixoDoGeo, StringComparison.Ordinal)
                ? $"{codigo}: descrição sem código."
                : $"{codigo}: descrição de código que não é do Geo; os códigos comuns são descritos pela API.";
        }

        foreach (IGrouping<string, string> repetido in descritos.GroupBy(static c => c).Where(static g => g.Key.Length > 0 && g.Skip(1).Any()))
        {
            yield return $"{repetido.Key}: descrição repetida.";
        }
    }

    private static IEnumerable<string> ConferirDescricao(DescricaoDeErro? descricao)
    {
        string codigo = CodigoSeguro(descricao?.Codigo);
        if (descricao is null || codigo.Length == 0)
        {
            return ["descricoes-de-erros.json: entrada com código ausente ou inválido."];
        }

        return
        [
            .. ConferirRequisitos(descricao, codigo),
            .. ConferirSecao(codigo, "oQueAconteceu", descricao.OQueAconteceu),
            .. ConferirSecao(codigo, "comoResolver", descricao.ComoResolver),
            .. descricao.OndeAparece is null ? [] : ConferirSecao(codigo, "ondeAparece", descricao.OndeAparece),
        ];
    }

    private static IEnumerable<string> ConferirRequisitos(DescricaoDeErro descricao, string codigo)
    {
        if (descricao.Requisitos is null || descricao.Requisitos.Any(static r => r is null || !RegrasDoTextoDaDescricao.Requisito().IsMatch(r)))
        {
            yield return $"{codigo}: requisitos devem usar UNI-REQ-NNNN.";
        }
    }

    private static IEnumerable<string> ConferirSecao(string codigo, string secao, IReadOnlyList<string?>? blocos)
    {
        if (blocos is null or { Count: 0 })
        {
            return [$"{codigo}: {secao} deve conter pelo menos um bloco."];
        }

        return blocos.SelectMany(ConferirBloco).Select(regra => $"{codigo}: {secao}, {regra}.");
    }

    private static IEnumerable<string> ConferirBloco(string? bloco) =>
        string.IsNullOrWhiteSpace(bloco) ? ["bloco vazio"] : RegrasDoTextoDaDescricao.Conferir(bloco);

    private static string CodigoSeguro(string? codigo) =>
        codigo is not null
        && RegrasDoTextoDaDescricao.CodigoPublico().IsMatch(codigo)
        && !RegrasDoTextoDaDescricao.Conferir(codigo).Any()
            ? codigo
            : string.Empty;
}
