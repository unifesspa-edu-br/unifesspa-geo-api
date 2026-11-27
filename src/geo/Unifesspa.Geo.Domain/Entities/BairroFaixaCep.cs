namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Faixa de CEP de um <see cref="Bairro"/> (origem <c>bairro_faixa</c> da DNE).
/// Reference data sem soft-delete (ADR-0092). Consistência/sobreposição da faixa
/// ficam na carga e na consulta.
/// </summary>
public sealed class BairroFaixaCep : EntityBase
{
    /// <summary>FK intra-banco para o <see cref="Bairro"/> (ADR-0054).</summary>
    public Guid BairroId { get; private set; }

    public string CepInicial { get; private set; } = string.Empty;

    public string CepFinal { get; private set; } = string.Empty;

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando a faixa some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private BairroFaixaCep()
    {
    }

    /// <summary>
    /// Importa uma faixa de CEP de Bairro. Valores já tipados (leitura tolerante na carga).
    /// Valida vínculo, limites da faixa e a versão da base em <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<BairroFaixaCep> Importar(
        Guid bairroId,
        FaixaDeCep faixa,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(bairroId, faixa, proveniencia);
        if (erro is not null)
        {
            return Result<BairroFaixaCep>.Failure(erro);
        }

        var entidade = new BairroFaixaCep { BairroId = bairroId, CepInicial = faixa.CepInicial!, CepFinal = faixa.CepFinal! };
        entidade.Aplicar(proveniencia);

        return Result<BairroFaixaCep>.Success(entidade);
    }

    /// <summary>
    /// Reaplica a proveniência de uma versão da base sobre uma faixa já existente (atualização
    /// pela chave natural da carga), preservando <see cref="EntityBase.Id"/> e a chave natural
    /// <c>(bairro_id, cep_inicial, cep_final)</c>. Valida antes de mutar.
    /// </summary>
    public Result Atualizar(ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(BairroId, new FaixaDeCep(CepInicial, CepFinal), proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid bairroId, FaixaDeCep faixa, ProvenienciaDaCarga proveniencia) =>
        bairroId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.BairroFaixaCepBairroObrigatorio, "Bairro da faixa de CEP é obrigatorio.")
            : !faixa.TemCepInicial
                ? new DomainError(GeoReferenceDataErrorCodes.BairroFaixaCepInicialObrigatorio, "CEP inicial da faixa é obrigatório.")
                : !faixa.TemCepFinal
                    ? new DomainError(GeoReferenceDataErrorCodes.BairroFaixaCepFinalObrigatorio, "CEP final da faixa é obrigatório.")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.BairroFaixaCepVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) da faixa de CEP é obrigatória.");

    private void Aplicar(ProvenienciaDaCarga proveniencia) => (VersaoDataset, Vigente) = proveniencia;
}
