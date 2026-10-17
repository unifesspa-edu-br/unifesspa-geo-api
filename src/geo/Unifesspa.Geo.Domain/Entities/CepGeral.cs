namespace Unifesspa.Geo.Domain.Entities;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// CEP geral oficial de uma <see cref="Cidade"/> ou de um <see cref="Distrito"/> —
/// o CEP único que os Correios atribuem à localidade inteira (na DNE, linha de
/// logradouro sem nome). Reference data sem soft-delete (ADR-0092). Diferencia o CEP
/// oficial de um número que apenas cai na faixa de CEP da cidade.
/// </summary>
/// <remarks>
/// O <see cref="Cep"/> é a chave natural UNIQUE. Com <see cref="DistritoId"/> nulo o
/// CEP é geral da cidade; preenchido, é geral do distrito (que pertence à mesma
/// cidade — coerência garantida pelo ETL).
/// </remarks>
public sealed class CepGeral : EntityBase
{
    /// <summary>CEP geral (8 dígitos; chave natural UNIQUE).</summary>
    public string Cep { get; private set; } = string.Empty;

    /// <summary>FK intra-banco para a <see cref="Cidade"/> (ADR-0054).</summary>
    public Guid CidadeId { get; private set; }

    /// <summary>FK opcional para o <see cref="Distrito"/>: preenchida quando o CEP é geral de distrito.</summary>
    public Guid? DistritoId { get; private set; }

    /// <summary>UF denormalizada da fonte (o vínculo forte é <see cref="CidadeId"/>).</summary>
    public string Uf { get; private set; } = string.Empty;

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    /// <summary>Coordenada geográfica (SRID 4326) — <c>geography(Point,4326)</c> (ADR-0091).</summary>
    public Point? Coordenada { get; private set; }

    /// <summary>Release DNE de origem (AAAAMM) — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando o CEP some da release vigente (stale do ETL).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private CepGeral()
    {
    }

    /// <summary>Importa um CEP geral a partir de valores já tipados (parse no ETL).</summary>
    public static Result<CepGeral> Importar(
        string cep,
        Guid cidadeId,
        Guid? distritoId,
        string uf,
        decimal? latitude,
        decimal? longitude,
        Point? coordenada,
        string versaoDataset,
        bool vigente = true)
    {
        ArgumentNullException.ThrowIfNull(cep);
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(versaoDataset);

        DomainError? erro = Validar(cep, cidadeId, versaoDataset);
        if (erro is not null)
        {
            return Result<CepGeral>.Failure(erro);
        }

        var entidade = new CepGeral { Cep = cep.Trim() };
        entidade.Aplicar(cidadeId, distritoId, uf, latitude, longitude, coordenada, versaoDataset, vigente);

        return Result<CepGeral>.Success(entidade);
    }

    /// <summary>
    /// Reaplica os dados de uma release sobre um CEP geral já existente (upsert in place
    /// do ETL), preservando <see cref="EntityBase.Id"/> e a chave natural <see cref="Cep"/>.
    /// Valida o mínimo <strong>antes</strong> de mutar.
    /// </summary>
    public Result Atualizar(
        Guid cidadeId,
        Guid? distritoId,
        string uf,
        decimal? latitude,
        decimal? longitude,
        Point? coordenada,
        string versaoDataset,
        bool vigente = true)
    {
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(versaoDataset);

        DomainError? erro = Validar(Cep, cidadeId, versaoDataset);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(cidadeId, distritoId, uf, latitude, longitude, coordenada, versaoDataset, vigente);

        return Result.Success();
    }

    private static DomainError? Validar(string cep, Guid cidadeId, string versaoDataset)
    {
        if (string.IsNullOrWhiteSpace(cep))
        {
            return new DomainError(GeoReferenceDataErrorCodes.CepGeralCepObrigatorio, "CEP geral é obrigatório.");
        }

        if (cidadeId == Guid.Empty)
        {
            return new DomainError(GeoReferenceDataErrorCodes.CepGeralCidadeObrigatoria, "Cidade do CEP geral é obrigatória.");
        }

        return string.IsNullOrWhiteSpace(versaoDataset)
            ? new DomainError(
                GeoReferenceDataErrorCodes.CepGeralVersaoDatasetObrigatoria,
                "Versão do dataset (proveniência) do CEP geral é obrigatória.")
            : null;
    }

    private void Aplicar(
        Guid cidadeId,
        Guid? distritoId,
        string uf,
        decimal? latitude,
        decimal? longitude,
        Point? coordenada,
        string versaoDataset,
        bool vigente)
    {
        CidadeId = cidadeId;
        DistritoId = distritoId;
        Uf = GeoTexto.NormalizarChaveMaiuscula(uf);
        Latitude = latitude;
        Longitude = longitude;
        Coordenada = coordenada;
        VersaoDataset = versaoDataset.Trim();
        Vigente = vigente;
    }
}
