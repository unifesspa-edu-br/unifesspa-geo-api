namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Faixa de CEP de uma <see cref="Cidade"/> (origem <c>cidade_faixa</c> da DNE).
/// Reference data sem soft-delete (ADR-0092). A consistência da faixa e a não-
/// sobreposição são responsabilidade da carga e da consulta.
/// </summary>
public sealed class CidadeFaixaCep : EntityBase
{
    /// <summary>FK intra-banco para a <see cref="Cidade"/> (ADR-0054).</summary>
    public Guid CidadeId { get; private set; }

    public string CepInicial { get; private set; } = string.Empty;

    public string CepFinal { get; private set; } = string.Empty;

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando a faixa some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private CidadeFaixaCep()
    {
    }

    /// <summary>
    /// Importa uma faixa de CEP de Cidade. Valores já tipados (leitura tolerante na carga).
    /// Valida vínculo, limites da faixa e a versão da base em <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<CidadeFaixaCep> Importar(
        Guid cidadeId,
        FaixaDeCep faixa,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(cidadeId, faixa, proveniencia);
        if (erro is not null)
        {
            return Result<CidadeFaixaCep>.Failure(erro);
        }

        var entidade = new CidadeFaixaCep { CidadeId = cidadeId, CepInicial = faixa.CepInicial!, CepFinal = faixa.CepFinal! };
        entidade.Aplicar(proveniencia);

        return Result<CidadeFaixaCep>.Success(entidade);
    }

    /// <summary>
    /// Reaplica a proveniência de uma versão da base sobre uma faixa já existente (atualização
    /// pela chave natural da carga), preservando <see cref="EntityBase.Id"/> e a chave natural
    /// <c>(cidade_id, cep_inicial, cep_final)</c>. Valida antes de mutar.
    /// </summary>
    public Result Atualizar(ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(CidadeId, new FaixaDeCep(CepInicial, CepFinal), proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid cidadeId, FaixaDeCep faixa, ProvenienciaDaCarga proveniencia) =>
        cidadeId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.CidadeFaixaCepCidadeObrigatoria, "Cidade da faixa de CEP é obrigatoria.")
            : !faixa.TemCepInicial
                ? new DomainError(GeoReferenceDataErrorCodes.CidadeFaixaCepInicialObrigatorio, "CEP inicial da faixa é obrigatório.")
                : !faixa.TemCepFinal
                    ? new DomainError(GeoReferenceDataErrorCodes.CidadeFaixaCepFinalObrigatorio, "CEP final da faixa é obrigatório.")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.CidadeFaixaCepVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) da faixa de CEP é obrigatória.");

    private void Aplicar(ProvenienciaDaCarga proveniencia) => (VersaoDataset, Vigente) = proveniencia;
}
