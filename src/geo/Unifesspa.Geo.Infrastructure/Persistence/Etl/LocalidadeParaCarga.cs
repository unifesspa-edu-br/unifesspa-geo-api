namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>
/// Distrito ou bairro já validado e com a cidade resolvida, pronto para ser criado ou
/// atualizado. A chave natural é o par (cidade, nome normalizado).
/// </summary>
internal readonly record struct LocalidadeParaCarga(
    Guid CidadeId,
    string Uf,
    string Nome,
    string NomeNormalizado,
    CoordenadaDne Coordenada,
    string? IdOrigemDne,
    string Versao)
{
    public string Chave => ChaveDe(CidadeId, NomeNormalizado);

    public static string ChaveDe(Guid cidadeId, string nomeNormalizado) => $"{cidadeId:N}|{nomeNormalizado}";

    public static LocalidadeParaCarga? De(
        LocalidadeDaFonte linha,
        Dictionary<int, Guid> cidadesPorIdDne,
        string versao,
        ContadorTabela contador)
    {
        string? nomeNormalizado = NormalizarObrigatorio(linha.NomeSemAcento ?? linha.Nome);
        if (nomeNormalizado is null)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }
        if (ResolverCidade(cidadesPorIdDne, linha.CidadeIdDne) is not Guid cidadeId)
        {
            contador.ContarOrfao(IdDaFonteParaRelatorio(linha.IdDne));
            return null;
        }
        return Montar(linha, cidadeId, nomeNormalizado, versao);
    }

    // Latitude e longitude saem do ponto já validado (limitado a ±90/±180): dado sujo
    // não pode estourar o numeric(9,6) da coluna.
    private static LocalidadeParaCarga Montar(LocalidadeDaFonte linha, Guid cidadeId, string nomeNormalizado, string versao) =>
        new(
            cidadeId,
            linha.Uf ?? string.Empty,
            linha.Nome ?? string.Empty,
            nomeNormalizado,
            CoordenadaDne.DoPontoValidado(linha.Latitude, linha.Longitude),
            linha.IdDne?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            versao);

    public Result<Distrito> CriarDistrito() =>
        Distrito.Importar(
            CidadeId, Uf, Nome, NomeNormalizado,
            Coordenada.Latitude, Coordenada.Longitude, Coordenada.Ponto, IdOrigemDne, Versao);

    public Result Atualizar(Distrito distrito) =>
        distrito.Atualizar(
            CidadeId, Uf, Nome, NomeNormalizado,
            Coordenada.Latitude, Coordenada.Longitude, Coordenada.Ponto, IdOrigemDne, Versao);

    public Result<Bairro> CriarBairro() =>
        Bairro.Importar(
            CidadeId, Uf, Nome, NomeNormalizado,
            Coordenada.Latitude, Coordenada.Longitude, Coordenada.Ponto, IdOrigemDne, Versao);

    public Result Atualizar(Bairro bairro) =>
        bairro.Atualizar(
            CidadeId, Uf, Nome, NomeNormalizado,
            Coordenada.Latitude, Coordenada.Longitude, Coordenada.Ponto, IdOrigemDne, Versao);

    private static string? NormalizarObrigatorio(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : GeoTexto.NormalizarTexto(valor);
}
