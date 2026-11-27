namespace Unifesspa.Geo.Domain.Entities;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// CEP exclusivo de grande usuário (origem <c>log_grande_usuario</c> da DNE) —
/// órgão/empresa com CEP próprio (ex.: UNESP). Reference data sem soft-delete
/// (ADR-0092). Carrega só <see cref="Cep"/> + <see cref="Nome"/> (+ proveniência):
/// <strong>não tem cidade/UF</strong> — a localização é resolvida por faixa de CEP
/// na consulta, não por FK aqui.
/// </summary>
/// <remarks>O <see cref="Cep"/> é a chave natural UNIQUE (um grande usuário por CEP).</remarks>
public sealed class CepGrandeUsuario : EntityBase
{
    /// <summary>CEP exclusivo (chave natural UNIQUE).</summary>
    public string Cep { get; private set; } = string.Empty;

    public string Nome { get; private set; } = string.Empty;

    /// <summary>Nome sem acentos (origem <c>*_sem_acento</c>), quando informado.</summary>
    public string? NomeNormalizado { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando o CEP some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private CepGrandeUsuario()
    {
    }

    /// <summary>
    /// Importa um CEP de grande usuário. Valores já tipados (leitura tolerante na carga). Valida o
    /// mínimo: chave natural (<paramref name="cep"/>), o nome em <paramref name="nome"/> e a
    /// versão da base em <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<CepGrandeUsuario> Importar(
        string cep,
        NomeComFormaDeBusca nome,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(cep, nome.Nome, proveniencia);
        if (erro is not null)
        {
            return Result<CepGrandeUsuario>.Failure(erro);
        }

        var entidade = new CepGrandeUsuario { Cep = cep.Trim() };
        entidade.Aplicar(nome, proveniencia);

        return Result<CepGrandeUsuario>.Success(entidade);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre um CEP de grande usuário já existente
    /// (atualização pela chave natural da carga), preservando <see cref="EntityBase.Id"/> e a
    /// chave natural <see cref="Cep"/>. Valida o mínimo <strong>antes</strong> de mutar.
    /// </summary>
    public Result Atualizar(NomeComFormaDeBusca nome, ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(Cep, nome.Nome, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(nome, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(string cep, string nome, ProvenienciaDaCarga proveniencia) =>
        string.IsNullOrWhiteSpace(cep)
            ? new DomainError(GeoReferenceDataErrorCodes.CepGrandeUsuarioCepObrigatorio, "CEP do grande usuário é obrigatório.")
            : string.IsNullOrWhiteSpace(nome)
                ? new DomainError(GeoReferenceDataErrorCodes.CepGrandeUsuarioNomeObrigatorio, "Nome do grande usuário é obrigatório.")
                : proveniencia.TemVersao
                    ? null
                    : new DomainError(
                        GeoReferenceDataErrorCodes.CepGrandeUsuarioVersaoDatasetObrigatoria,
                        "Versão do dataset (proveniência) do grande usuário é obrigatória.");

    private void Aplicar(NomeComFormaDeBusca nome, ProvenienciaDaCarga proveniencia)
    {
        (Nome, NomeNormalizado) = nome;
        (VersaoDataset, Vigente) = proveniencia;
    }
}
