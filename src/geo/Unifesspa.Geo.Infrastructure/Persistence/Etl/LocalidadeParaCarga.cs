namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
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
    NomeComFormaDeBusca Nome,
    PosicaoGeografica Posicao,
    string? IdOrigemDne,
    ProvenienciaDaCarga Proveniencia)
{
    public string Chave => ChaveDe(CidadeId, Nome.NomeNormalizado!);

    public static string ChaveDe(Guid cidadeId, string nomeNormalizado) => $"{cidadeId:N}|{nomeNormalizado}";

    public static LocalidadeParaCarga? De(
        LocalidadeDaFonte linha,
        Dictionary<int, Guid> cidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
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
        return Montar(linha, cidadeId, nomeNormalizado, proveniencia);
    }

    // Latitude e longitude só são gravadas dentro de ±90/±180 (posição validada): dado sujo
    // não pode estourar o numeric(9,6) da coluna.
    private static LocalidadeParaCarga Montar(
        LocalidadeDaFonte linha,
        Guid cidadeId,
        string nomeNormalizado,
        ProvenienciaDaCarga proveniencia) =>
        new(
            cidadeId,
            linha.Uf ?? string.Empty,
            new NomeComFormaDeBusca(linha.Nome ?? string.Empty, nomeNormalizado),
            CoordenadaDne.Ler(linha.Latitude, linha.Longitude),
            linha.IdDne?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            proveniencia);

    public Result<Distrito> CriarDistrito() =>
        Distrito.Importar(CidadeId, Uf, Nome, Posicao, IdOrigemDne, Proveniencia);

    public Result Atualizar(Distrito distrito) =>
        distrito.Atualizar(CidadeId, Uf, Nome, Posicao, IdOrigemDne, Proveniencia);

    public Result<Bairro> CriarBairro() =>
        Bairro.Importar(CidadeId, Uf, Nome, Posicao, IdOrigemDne, Proveniencia);

    public Result Atualizar(Bairro bairro) =>
        bairro.Atualizar(CidadeId, Uf, Nome, Posicao, IdOrigemDne, Proveniencia);

    private static string? NormalizarObrigatorio(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : GeoTexto.NormalizarTexto(valor);
}
