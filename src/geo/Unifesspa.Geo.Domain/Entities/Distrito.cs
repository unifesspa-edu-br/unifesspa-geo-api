namespace Unifesspa.Geo.Domain.Entities;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Distrito — nível intermediário situado numa <see cref="Cidade"/>. Reference data
/// sem soft-delete (ADR-0092). Não tem código IBGE na fonte: a chave natural é
/// <c>(cidade_id, nome_normalizado)</c>; o id sequencial da DNE é guardado apenas
/// como <see cref="IdOrigemDne"/> (referência de origem, instável entre versões).
/// </summary>
/// <remarks>
/// <see cref="NomeNormalizado"/> é <strong>obrigatório</strong> porque compõe a
/// chave natural: em PostgreSQL um UNIQUE com coluna nula admite múltiplos nulos,
/// o que furaria a idempotência da gravação pela chave natural — por isso o valor não pode ser nulo.
/// </remarks>
public sealed class Distrito : EntityBase
{
    /// <summary>FK intra-banco para a <see cref="Cidade"/> (ADR-0054).</summary>
    public Guid CidadeId { get; private set; }

    /// <summary>UF denormalizada da fonte (o vínculo forte é <see cref="CidadeId"/>).</summary>
    public string Uf { get; private set; } = string.Empty;

    public string Nome { get; private set; } = string.Empty;

    /// <summary>Nome sem acentos (origem <c>*_sem_acento</c>) — compõe a chave natural; não nulo.</summary>
    public string NomeNormalizado { get; private set; } = string.Empty;

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    /// <summary>Coordenada geográfica (SRID 4326) — <c>geography(Point,4326)</c> (ADR-0091).</summary>
    public Point? Coordenada { get; private set; }

    /// <summary>Id sequencial de origem na DNE — referência instável, não é identidade nem único.</summary>
    public string? IdOrigemDne { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando a chave natural some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private Distrito()
    {
    }

    /// <summary>
    /// Importa um Distrito a partir de valores já tipados (leitura tolerante na carga). Valida o
    /// mínimo: <paramref name="cidadeId"/>, o nome e a forma de busca em <paramref name="nome"/>
    /// (compõe a chave natural) e a versão da base em <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<Distrito> Importar(
        Guid cidadeId,
        string uf,
        NomeComFormaDeBusca nome,
        PosicaoGeografica posicao,
        string? idOrigemDne,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(cidadeId, nome, proveniencia);
        if (erro is not null)
        {
            return Result<Distrito>.Failure(erro);
        }

        var distrito = new Distrito();
        distrito.Aplicar(cidadeId, uf, nome, posicao, idOrigemDne, proveniencia);

        return Result<Distrito>.Success(distrito);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre um Distrito já existente (atualização pela
    /// chave natural da carga), preservando <see cref="EntityBase.Id"/> (referenciado por faixas
    /// e por <see cref="Logradouro"/>) e a chave natural <c>(cidade_id, nome_normalizado)</c>.
    /// Valida o mínimo <strong>antes</strong> de mutar — em falha, o estado não é tocado.
    /// </summary>
    public Result Atualizar(
        Guid cidadeId,
        string uf,
        NomeComFormaDeBusca nome,
        PosicaoGeografica posicao,
        string? idOrigemDne,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(cidadeId, nome, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(cidadeId, uf, nome, posicao, idOrigemDne, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid cidadeId, NomeComFormaDeBusca nome, ProvenienciaDaCarga proveniencia) =>
        cidadeId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.DistritoCidadeObrigatoria, "Cidade do Distrito é obrigatória.")
            : string.IsNullOrWhiteSpace(nome.Nome)
                ? new DomainError(GeoReferenceDataErrorCodes.DistritoNomeObrigatorio, "Nome do Distrito é obrigatório.")
                : string.IsNullOrWhiteSpace(nome.NomeNormalizado)
                    ? new DomainError(
                        GeoReferenceDataErrorCodes.DistritoNomeNormalizadoObrigatorio,
                        "Nome normalizado do Distrito é obrigatório (compõe a chave natural).")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.DistritoVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) do Distrito é obrigatória.");

    private void Aplicar(
        Guid cidadeId,
        string uf,
        NomeComFormaDeBusca nome,
        PosicaoGeografica posicao,
        string? idOrigemDne,
        ProvenienciaDaCarga proveniencia)
    {
        CidadeId = cidadeId;
        Uf = GeoTexto.NormalizarChaveMaiuscula(uf);
        Nome = nome.Nome;
        NomeNormalizado = nome.NomeNormalizado!;
        (Latitude, Longitude, Coordenada) = posicao;
        IdOrigemDne = GeoTexto.NormalizarOpcional(idOrigemDne);
        (VersaoDataset, Vigente) = proveniencia;
    }
}
