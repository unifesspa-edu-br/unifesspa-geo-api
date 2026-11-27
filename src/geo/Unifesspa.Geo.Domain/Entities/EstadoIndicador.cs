namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Satélite socioeconômico IBGE de um <see cref="Estado"/> (1:1). Reference data
/// sem soft-delete (ADR-0092). Todos os indicadores são <c>nullable</c>: a fonte
/// traz <c>'-'</c>/vazio para dado ausente e o ETL degrada para
/// <see langword="null"/> (leitura tolerante na carga) — só o vínculo e a proveniência
/// são obrigatórios.
/// </summary>
public sealed class EstadoIndicador : EntityBase
{
    /// <summary>FK 1:1 para o <see cref="Estado"/> (UNIQUE, ADR-0054).</summary>
    public Guid EstadoId { get; private set; }

    public string? Gentilico { get; private set; }

    public string? Governador { get; private set; }

    public decimal? AreaKm2 { get; private set; }

    public int? PopulacaoResidente2022 { get; private set; }

    public decimal? DensidadeDemografica { get; private set; }

    public int? MatriculasEnsinoFundamental2023 { get; private set; }

    public decimal? Idh { get; private set; }

    /// <summary>Receitas brutas (≈bilhões) — <c>decimal(18,2)</c>.</summary>
    public decimal? ReceitasBrutas { get; private set; }

    /// <summary>Despesas brutas (≈bilhões) — <c>decimal(18,2)</c>.</summary>
    public decimal? DespesasBrutas { get; private set; }

    public int? RendimentoMensalPerCapita { get; private set; }

    public int? TotalVeiculos2023 { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando o vínculo some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private EstadoIndicador()
    {
    }

    /// <summary>
    /// Importa o indicador de um Estado. Valores já tipados (leitura tolerante na carga).
    /// Valida só o mínimo: vínculo (<paramref name="estadoId"/>) e a versão da base em
    /// <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<EstadoIndicador> Importar(
        Guid estadoId,
        string? gentilico,
        string? governador,
        MetricasDoEstado metricas,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(metricas);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(estadoId, proveniencia);
        if (erro is not null)
        {
            return Result<EstadoIndicador>.Failure(erro);
        }

        var indicador = new EstadoIndicador { EstadoId = estadoId };
        indicador.Aplicar(gentilico, governador, metricas, proveniencia);

        return Result<EstadoIndicador>.Success(indicador);
    }

    /// <summary>
    /// Reaplica os indicadores de uma versão da base sobre o satélite já existente (atualização
    /// pela chave natural da carga), preservando <see cref="EntityBase.Id"/> e o vínculo
    /// <see cref="EstadoId"/>. Valida o mínimo <strong>antes</strong> de mutar.
    /// </summary>
    public Result Atualizar(
        string? gentilico,
        string? governador,
        MetricasDoEstado metricas,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(metricas);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(EstadoId, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(gentilico, governador, metricas, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid estadoId, ProvenienciaDaCarga proveniencia) =>
        estadoId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.EstadoIndicadorEstadoObrigatorio, "Estado do indicador é obrigatório.")
            : proveniencia.TemVersao
                ? null
                : new DomainError(
                    GeoReferenceDataErrorCodes.EstadoIndicadorVersaoDatasetObrigatoria,
                    "Versão do dataset (proveniência) do indicador de Estado é obrigatória.");

    private void Aplicar(
        string? gentilico,
        string? governador,
        MetricasDoEstado metricas,
        ProvenienciaDaCarga proveniencia)
    {
        Gentilico = GeoTexto.NormalizarOpcional(gentilico);
        Governador = GeoTexto.NormalizarOpcional(governador);
        AreaKm2 = metricas.AreaKm2;
        PopulacaoResidente2022 = metricas.PopulacaoResidente2022;
        DensidadeDemografica = metricas.DensidadeDemografica;
        MatriculasEnsinoFundamental2023 = metricas.MatriculasEnsinoFundamental2023;
        Idh = metricas.Idh;
        ReceitasBrutas = metricas.ReceitasBrutas;
        DespesasBrutas = metricas.DespesasBrutas;
        RendimentoMensalPerCapita = metricas.RendimentoMensalPerCapita;
        TotalVeiculos2023 = metricas.TotalVeiculos2023;
        (VersaoDataset, Vigente) = proveniencia;
    }
}
