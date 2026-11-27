namespace Unifesspa.Geo.Domain.Entities;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
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

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando o CEP some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private CepGeral()
    {
    }

    /// <summary>Importa um CEP geral a partir de valores já tipados (leitura tolerante na carga).</summary>
    public static Result<CepGeral> Importar(
        string cep,
        Guid cidadeId,
        Guid? distritoId,
        string uf,
        PosicaoGeografica posicao,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(cep, cidadeId, proveniencia);
        if (erro is not null)
        {
            return Result<CepGeral>.Failure(erro);
        }

        var entidade = new CepGeral { Cep = cep.Trim() };
        entidade.Aplicar(cidadeId, distritoId, uf, posicao, proveniencia);

        return Result<CepGeral>.Success(entidade);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre um CEP geral já existente (atualização pela
    /// chave natural da carga), preservando <see cref="EntityBase.Id"/> e a chave natural
    /// <see cref="Cep"/>. Valida o mínimo <strong>antes</strong> de mutar.
    /// </summary>
    public Result Atualizar(
        Guid cidadeId,
        Guid? distritoId,
        string uf,
        PosicaoGeografica posicao,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(uf);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(Cep, cidadeId, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(cidadeId, distritoId, uf, posicao, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(string cep, Guid cidadeId, ProvenienciaDaCarga proveniencia) =>
        string.IsNullOrWhiteSpace(cep)
            ? new DomainError(GeoReferenceDataErrorCodes.CepGeralCepObrigatorio, "CEP geral é obrigatório.")
            : cidadeId == Guid.Empty
                ? new DomainError(GeoReferenceDataErrorCodes.CepGeralCidadeObrigatoria, "Cidade do CEP geral é obrigatória.")
                : proveniencia.TemVersao
                    ? null
                    : new DomainError(
                        GeoReferenceDataErrorCodes.CepGeralVersaoDatasetObrigatoria,
                        "Versão do dataset (proveniência) do CEP geral é obrigatória.");

    private void Aplicar(
        Guid cidadeId,
        Guid? distritoId,
        string uf,
        PosicaoGeografica posicao,
        ProvenienciaDaCarga proveniencia)
    {
        CidadeId = cidadeId;
        DistritoId = distritoId;
        Uf = GeoTexto.NormalizarChaveMaiuscula(uf);
        (Latitude, Longitude, Coordenada) = posicao;
        (VersaoDataset, Vigente) = proveniencia;
    }
}
