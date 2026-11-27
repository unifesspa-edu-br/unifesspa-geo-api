namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Semeadura determinística (chaves naturais fixas) de dados de referência do Geo para os
/// testes de API. Cada teste apaga tudo antes de semear: a collection é serial e
/// compartilhada, então o cenário não pode depender do estado deixado por outro teste.
/// Estado, cidade e logradouro são montados pelos construtores de teste próprios.
/// </summary>
internal static partial class GeoReferenceSeed
{
    public const string Versao = "202601";

    public static ProvenienciaDaCarga Proveniencia(bool vigente = true) => new(Versao, vigente);

    /// <summary>GET por rota relativa ou absoluta (satisfaz CA2234 — <c>GetAsync(Uri)</c>).</summary>
    public static Task<HttpResponseMessage> Obter(HttpClient client, string rota)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.GetAsync(new Uri(rota, UriKind.RelativeOrAbsolute));
    }

    /// <summary>
    /// Apaga toda a hierarquia Geo. <c>TRUNCATE pais CASCADE</c> derruba
    /// estado/cidade/indicador/faixas/logradouro (ligados por FK); <c>cep_grande_usuario</c>
    /// e <c>logradouro_complemento</c> não têm FK para a hierarquia (são atributos do
    /// CEP), então entram explicitamente no TRUNCATE — senão acumulam entre testes
    /// seriais e furam os índices UNIQUE de CEP.
    /// </summary>
    public static async Task LimparAsync(GeoPostgisFixture fixture)
    {
        await using GeoDbContext ctx = fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE pais, cep_grande_usuario, logradouro_complemento CASCADE");
    }

    public static Pais NovoBrasil() =>
        GeoTestKeys.Ok(Pais.Importar("BRA", "BR", "Brasil", default, Proveniencia()));

    public static Distrito NovoDistrito(
        Guid cidadeId,
        string uf,
        string nome,
        decimal? latitude = null,
        decimal? longitude = null,
        bool vigente = true) =>
        GeoTestKeys.Ok(Distrito.Importar(
            cidadeId, uf, nome, Normalizar(nome), latitude, longitude, null, null, Versao, vigente));

    public static Bairro NovoBairro(
        Guid cidadeId,
        string uf,
        string nome,
        decimal? latitude = null,
        decimal? longitude = null,
        bool vigente = true) =>
        GeoTestKeys.Ok(Bairro.Importar(
            cidadeId, uf, nome, Normalizar(nome), latitude, longitude, null, null, Versao, vigente));

    public static LogradouroComplemento NovoComplemento(string cep, string complemento, bool vigente = true) =>
        GeoTestKeys.Ok(LogradouroComplemento.Importar(cep, complemento, Normalizar(complemento), Versao, vigente));

    public static CepGrandeUsuario NovoGrandeUsuario(string cep, string nome, bool vigente = true) =>
        GeoTestKeys.Ok(CepGrandeUsuario.Importar(cep, nome, Normalizar(nome), Versao, vigente));

    public static CepGeral NovoCepGeral(
        string cep,
        Guid cidadeId,
        string uf,
        Guid? distritoId = null,
        bool vigente = true) =>
        GeoTestKeys.Ok(CepGeral.Importar(cep, cidadeId, distritoId, uf, null, null, null, Versao, vigente));

    public static CidadeFaixaCep NovaCidadeFaixa(Guid cidadeId, string cepInicial, string cepFinal, bool vigente = true) =>
        GeoTestKeys.Ok(CidadeFaixaCep.Importar(cidadeId, cepInicial, cepFinal, Versao, vigente));

    public static BairroFaixaCep NovaBairroFaixa(Guid bairroId, string cepInicial, string cepFinal, bool vigente = true) =>
        GeoTestKeys.Ok(BairroFaixaCep.Importar(bairroId, cepInicial, cepFinal, Versao, vigente));

    public static DistritoFaixaCep NovaDistritoFaixa(Guid distritoId, string cepInicial, string cepFinal, bool vigente = true) =>
        GeoTestKeys.Ok(DistritoFaixaCep.Importar(distritoId, cepInicial, cepFinal, Versao, vigente));

    public static CidadeIndicador NovoIndicador(
        Guid cidadeId,
        string? gentilico = null,
        decimal? areaKm2 = null,
        int? populacaoResidente = null,
        decimal? densidadeDemografica = null,
        decimal? idh = null,
        string? aniversario = null) =>
        GeoTestKeys.Ok(CidadeIndicador.Importar(
            cidadeId, gentilico, null, areaKm2, populacaoResidente, densidadeDemografica,
            null, idh, null, null, null, null, aniversario, Versao));

    /// <summary>Extrai a URL de um <c>rel</c> específico do header <c>Link</c> (RFC 5988/8288), ou <see langword="null"/>.</summary>
    public static string? ExtrairLink(HttpResponseMessage resposta, string rel)
    {
        if (!resposta.Headers.TryGetValues("Link", out IEnumerable<string>? values))
        {
            return null;
        }

        string header = string.Join(", ", values);
        return LinkHeaderRegex().Matches(header)
            .Where(match => string.Equals(match.Groups["rel"].Value, rel, StringComparison.Ordinal))
            .Select(match => match.Groups["url"].Value)
            .FirstOrDefault();
    }

    // Remove os diacríticos (como a fonte faz em *_sem_acento). O ToLower fica por
    // conta da própria entidade (GeoTexto.NormalizarBuscaOpcional) — evita CA1308 aqui.
    internal static string Normalizar(string nome)
    {
        string decomposto = nome.Normalize(NormalizationForm.FormD);
        return new string([.. decomposto.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
    }

    [GeneratedRegex("<(?<url>[^>]+)>;\\s*rel=\"(?<rel>[^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkHeaderRegex();
}
