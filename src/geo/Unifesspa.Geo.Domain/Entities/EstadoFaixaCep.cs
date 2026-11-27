namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Faixa de CEP de um <see cref="Estado"/> (origem <c>estado_faixa</c> da DNE) —
/// uma UF pode ter várias (capital e interior). Reference data sem soft-delete
/// (ADR-0092). A <see cref="Descricao"/> preserva a coluna <c>regiao</c> da fonte
/// (ex.: "Capital: Rio Branco").
/// </summary>
public sealed class EstadoFaixaCep : EntityBase
{
    /// <summary>FK intra-banco para o <see cref="Estado"/> (ADR-0054).</summary>
    public Guid EstadoId { get; private set; }

    public string CepInicial { get; private set; } = string.Empty;

    public string CepFinal { get; private set; } = string.Empty;

    public string? Descricao { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando a faixa some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private EstadoFaixaCep()
    {
    }

    /// <summary>
    /// Importa uma faixa de CEP de Estado. Valores já tipados (leitura tolerante na carga).
    /// Valida vínculo, limites da faixa e a versão da base em <paramref name="proveniencia"/>.
    /// A consistência <c>cep_inicial ≤ cep_final</c> e a não-sobreposição são responsabilidade
    /// da carga e da consulta — a carga em lote é tolerante por construção (ADR-0092).
    /// </summary>
    public static Result<EstadoFaixaCep> Importar(
        Guid estadoId,
        FaixaDeCep faixa,
        string? descricao,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(estadoId, faixa, proveniencia);
        if (erro is not null)
        {
            return Result<EstadoFaixaCep>.Failure(erro);
        }

        var entidade = new EstadoFaixaCep { EstadoId = estadoId, CepInicial = faixa.CepInicial!, CepFinal = faixa.CepFinal! };
        entidade.Aplicar(descricao, proveniencia);

        return Result<EstadoFaixaCep>.Success(entidade);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre a faixa já existente (atualização pela
    /// chave natural da carga <c>(estado_id, cep_inicial, cep_final)</c>), preservando
    /// <see cref="EntityBase.Id"/>. A chave natural não muda — só metadados e proveniência.
    /// </summary>
    public Result Atualizar(string? descricao, ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(EstadoId, new FaixaDeCep(CepInicial, CepFinal), proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(descricao, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid estadoId, FaixaDeCep faixa, ProvenienciaDaCarga proveniencia) =>
        estadoId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.EstadoFaixaCepEstadoObrigatorio, "Estado da faixa de CEP é obrigatório.")
            : !faixa.TemCepInicial
                ? new DomainError(GeoReferenceDataErrorCodes.EstadoFaixaCepInicialObrigatorio, "CEP inicial da faixa é obrigatório.")
                : !faixa.TemCepFinal
                    ? new DomainError(GeoReferenceDataErrorCodes.EstadoFaixaCepFinalObrigatorio, "CEP final da faixa é obrigatório.")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.EstadoFaixaCepVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) da faixa de CEP é obrigatória.");

    private void Aplicar(string? descricao, ProvenienciaDaCarga proveniencia)
    {
        Descricao = GeoTexto.NormalizarOpcional(descricao);
        (VersaoDataset, Vigente) = proveniencia;
    }
}
