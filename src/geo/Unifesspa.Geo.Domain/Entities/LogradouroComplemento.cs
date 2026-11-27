namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Complemento de endereçamento por CEP (origem <c>log_complemento</c> da DNE) —
/// ex.: lado par/ímpar, faixa de numeração. Reference data sem soft-delete
/// (ADR-0092). É um atributo do <strong>CEP</strong>, não de um logradouro
/// específico: o mesmo CEP comporta vários complementos e nenhum se vincula a um
/// único logradouro — por isso <strong>não há FK para <see cref="Logradouro"/></strong>.
/// </summary>
/// <remarks>
/// O <see cref="Cep"/> é indexado e <strong>não único</strong> (compartilhado). A
/// idempotência da gravação pela chave natural vem da chave <c>(cep, complemento_normalizado)</c>, que é
/// UNIQUE; <see cref="ComplementoNormalizado"/> é <c>NOT NULL</c> por compô-la.
/// </remarks>
public sealed class LogradouroComplemento : EntityBase
{
    /// <summary>CEP de 8 dígitos — indexado, NÃO único (compartilhado).</summary>
    public string Cep { get; private set; } = string.Empty;

    public string Complemento { get; private set; } = string.Empty;

    /// <summary>Complemento sem acentos — compõe a chave natural; não nulo.</summary>
    public string ComplementoNormalizado { get; private set; } = string.Empty;

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando a chave natural some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private LogradouroComplemento()
    {
    }

    /// <summary>
    /// Importa um complemento por CEP. Valores já tipados (leitura tolerante na carga). Valida o
    /// mínimo: chave natural (<paramref name="cep"/>, <paramref name="complementoNormalizado"/>),
    /// texto e a versão da base em <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<LogradouroComplemento> Importar(
        string cep,
        string complemento,
        string complementoNormalizado,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(complemento);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(cep, complemento, complementoNormalizado, proveniencia);
        if (erro is not null)
        {
            return Result<LogradouroComplemento>.Failure(erro);
        }

        var entidade = new LogradouroComplemento { Cep = cep.Trim() };
        entidade.Aplicar(complemento, complementoNormalizado, proveniencia);

        return Result<LogradouroComplemento>.Success(entidade);
    }

    private static DomainError? Validar(
        string cep,
        string complemento,
        string complementoNormalizado,
        ProvenienciaDaCarga proveniencia) =>
        string.IsNullOrWhiteSpace(cep)
            ? new DomainError(GeoReferenceDataErrorCodes.LogradouroComplementoCepObrigatorio, "CEP do complemento é obrigatório.")
            : string.IsNullOrWhiteSpace(complemento)
                ? new DomainError(GeoReferenceDataErrorCodes.LogradouroComplementoObrigatorio, "Texto do complemento é obrigatório.")
                : string.IsNullOrWhiteSpace(complementoNormalizado)
                    ? new DomainError(
                        GeoReferenceDataErrorCodes.LogradouroComplementoNormalizadoObrigatorio,
                        "Complemento normalizado é obrigatório (compõe a chave de upsert).")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.LogradouroComplementoVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) do complemento é obrigatória.");

    private void Aplicar(string complemento, string complementoNormalizado, ProvenienciaDaCarga proveniencia)
    {
        Complemento = complemento.Trim();
        ComplementoNormalizado = GeoTexto.NormalizarTexto(complementoNormalizado);
        (VersaoDataset, Vigente) = proveniencia;
    }
}
