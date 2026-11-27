namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Topo da hierarquia geográfica do Geo. Reference data de origem externa
/// (IBGE/DNE) — carregado por ETL, sem CRUD de usuário e sem soft-delete
/// (ADR-0092): deriva de <see cref="EntityBase"/> puro. Em produção é populado
/// <strong>só com o Brasil</strong> (a estrutura já comporta países estrangeiros).
/// </summary>
/// <remarks>
/// Chave natural <see cref="SiglaIso"/> (ISO 3166-1 alpha-3, ex.: <c>BRA</c>) —
/// base da atualização pela chave natural na carga. Toda entidade de referência carrega a
/// proveniência da carga (<see cref="VersaoDataset"/> + <see cref="Vigente"/>),
/// distinta de qualquer atributo de atividade do próprio dado.
/// </remarks>
public sealed class Pais : EntityBase
{
    /// <summary>Sigla ISO 3166-1 alpha-3 (chave natural, ex.: <c>BRA</c>).</summary>
    public string SiglaIso { get; private set; } = string.Empty;

    /// <summary>Sigla curta de uso corrente (ex.: <c>BR</c>).</summary>
    public string Sigla { get; private set; } = string.Empty;

    public string Nome { get; private set; } = string.Empty;

    /// <summary>Código do país no Banco Central (BCB), quando informado.</summary>
    public string? CodigoBcb { get; private set; }

    /// <summary>Código do país na Receita Federal (RFB), quando informado.</summary>
    public string? CodigoRfb { get; private set; }

    /// <summary>Código do país no SPED, quando informado.</summary>
    public string? CodigoSped { get; private set; }

    /// <summary>Código do país no Siscomex, quando informado.</summary>
    public string? CodigoSiscomex { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary>
    /// <see langword="false"/> quando a chave natural deixou de aparecer na versão
    /// vigente, sem remover a linha (política de registros não vigentes da carga). Distinto de
    /// qualquer indicador de atividade do dado.
    /// </summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private Pais()
    {
    }

    /// <summary>
    /// Importa um País a partir de valores já tipados (a leitura tolerante mora na carga).
    /// Valida só o mínimo: chave natural (<paramref name="siglaIso"/>), <paramref name="nome"/> e
    /// a versão da base em <paramref name="proveniencia"/> não vazios — dado externo
    /// autoritativo não tem regra de negócio mutável.
    /// </summary>
    public static Result<Pais> Importar(
        string siglaIso,
        string sigla,
        string nome,
        CodigosDoPaisNosCadastrosFederais codigos,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(sigla);
        ArgumentNullException.ThrowIfNull(nome);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(siglaIso, nome, proveniencia);
        if (erro is not null)
        {
            return Result<Pais>.Failure(erro);
        }

        var pais = new Pais { SiglaIso = GeoTexto.NormalizarChaveMaiuscula(siglaIso) };
        pais.Aplicar(sigla, nome, codigos, proveniencia);

        return Result<Pais>.Success(pais);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre um País já existente (atualização pela
    /// chave natural da carga), preservando <see cref="EntityBase.Id"/> e a chave natural
    /// <see cref="SiglaIso"/>. Valida o mínimo <strong>antes</strong> de mutar — em falha, o
    /// estado da entidade não é tocado (atomicidade da mutação).
    /// </summary>
    public Result Atualizar(
        string sigla,
        string nome,
        CodigosDoPaisNosCadastrosFederais codigos,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(sigla);
        ArgumentNullException.ThrowIfNull(nome);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(SiglaIso, nome, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(sigla, nome, codigos, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(string siglaIso, string nome, ProvenienciaDaCarga proveniencia) =>
        string.IsNullOrWhiteSpace(siglaIso)
            ? new DomainError(GeoReferenceDataErrorCodes.PaisSiglaIsoObrigatoria, "Sigla ISO do País é obrigatória.")
            : string.IsNullOrWhiteSpace(nome)
                ? new DomainError(GeoReferenceDataErrorCodes.PaisNomeObrigatorio, "Nome do País é obrigatório.")
                : proveniencia.TemVersao
                    ? null
                    : new DomainError(
                        GeoReferenceDataErrorCodes.PaisVersaoDatasetObrigatoria,
                        "Versão do dataset (proveniência) do País é obrigatória.");

    private void Aplicar(
        string sigla,
        string nome,
        CodigosDoPaisNosCadastrosFederais codigos,
        ProvenienciaDaCarga proveniencia)
    {
        Sigla = GeoTexto.NormalizarChaveMaiuscula(sigla);
        Nome = nome.Trim();
        (CodigoBcb, CodigoRfb, CodigoSped, CodigoSiscomex) = codigos;
        (VersaoDataset, Vigente) = proveniencia;
    }
}
