namespace Unifesspa.Geo.Domain.Entities;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Unidade federativa (estado/UF), segundo nível da hierarquia geográfica do Geo,
/// situada num <see cref="Pais"/>. Reference data sem soft-delete (ADR-0092) —
/// deriva de <see cref="EntityBase"/> puro.
/// </summary>
/// <remarks>
/// Chave natural <see cref="Uf"/> (UNIQUE) — base da atualização pela chave natural na carga.
/// A faixa geral de CEP da UF fica em <see cref="CepInicial"/>/<see cref="CepFinal"/>;
/// as múltiplas faixas (capital/interior) vão na entidade <see cref="EstadoFaixaCep"/>.
/// Coordenada em <c>geography(Point,4326)</c> (ADR-0091); lat/long também
/// preservados em <see cref="Latitude"/>/<see cref="Longitude"/>.
/// </remarks>
public sealed class Estado : EntityBase
{
    /// <summary>FK intra-banco para o <see cref="Pais"/> (ADR-0054).</summary>
    public Guid PaisId { get; private set; }

    /// <summary>Sigla da unidade federativa (chave natural, ex.: <c>PA</c>).</summary>
    public string Uf { get; private set; } = string.Empty;

    public string Nome { get; private set; } = string.Empty;

    /// <summary>Nome sem acentos (origem <c>*_sem_acento</c> da DNE) — acelera busca futura.</summary>
    public string? NomeNormalizado { get; private set; }

    /// <summary>
    /// Chave técnica gerada pelo banco para ordenação alfabética server-side. Usa
    /// <see cref="NomeNormalizado"/> quando presente e fallback normalizado de
    /// <see cref="Nome"/> quando a fonte não trouxe a coluna de busca.
    /// </summary>
    public string NomeOrdenacao { get; private set; } = string.Empty;

    public string? Regiao { get; private set; }

    public string? Capital { get; private set; }

    /// <summary>Código IBGE da UF (2 dígitos), quando informado.</summary>
    public string? CodigoIbge { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    /// <summary>Coordenada geográfica (SRID 4326) — <c>geography(Point,4326)</c> (ADR-0091).</summary>
    public Point? Coordenada { get; private set; }

    /// <summary>Início da faixa geral de CEP da UF (origem <c>faixa_ini</c>).</summary>
    public string? CepInicial { get; private set; }

    /// <summary>Fim da faixa geral de CEP da UF (origem <c>faixa_fim</c>).</summary>
    public string? CepFinal { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando a UF some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private Estado()
    {
    }

    /// <summary>
    /// Importa um Estado a partir de valores já tipados (a leitura tolerante mora na carga).
    /// Valida só o mínimo: o país e a UF em <paramref name="uf"/>, o nome em
    /// <paramref name="nome"/> e a versão da base em <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<Estado> Importar(
        UfNoPais uf,
        NomeComFormaDeBusca nome,
        ClassificacaoDaUfNoIbge classificacao,
        string? capital,
        PosicaoGeografica posicao,
        FaixaDeCep faixaGeral,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(uf.PaisId, uf.Sigla, nome.Nome, proveniencia);
        if (erro is not null)
        {
            return Result<Estado>.Failure(erro);
        }

        var estado = new Estado { Uf = uf.Sigla };
        estado.Aplicar(uf.PaisId, nome, classificacao, capital, posicao, faixaGeral, proveniencia);

        return Result<Estado>.Success(estado);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre um Estado já existente (atualização pela
    /// chave natural da carga), preservando <see cref="EntityBase.Id"/> (referenciado por
    /// Cidade, indicador e faixas) e a chave natural <see cref="Uf"/>. Valida o mínimo
    /// <strong>antes</strong> de mutar — em falha, o estado não é tocado.
    /// </summary>
    public Result Atualizar(
        Guid paisId,
        NomeComFormaDeBusca nome,
        ClassificacaoDaUfNoIbge classificacao,
        string? capital,
        PosicaoGeografica posicao,
        FaixaDeCep faixaGeral,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(paisId, Uf, nome.Nome, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(paisId, nome, classificacao, capital, posicao, faixaGeral, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid paisId, string uf, string nome, ProvenienciaDaCarga proveniencia) =>
        paisId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.EstadoPaisObrigatorio, "País do Estado é obrigatório.")
            : string.IsNullOrWhiteSpace(uf)
                ? new DomainError(GeoReferenceDataErrorCodes.EstadoUfObrigatoria, "UF do Estado é obrigatória.")
                : string.IsNullOrWhiteSpace(nome)
                    ? new DomainError(GeoReferenceDataErrorCodes.EstadoNomeObrigatorio, "Nome do Estado é obrigatório.")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.EstadoVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) do Estado é obrigatória.");

    private void Aplicar(
        Guid paisId,
        NomeComFormaDeBusca nome,
        ClassificacaoDaUfNoIbge classificacao,
        string? capital,
        PosicaoGeografica posicao,
        FaixaDeCep faixaGeral,
        ProvenienciaDaCarga proveniencia)
    {
        PaisId = paisId;
        (Nome, NomeNormalizado) = nome;
        (CodigoIbge, Regiao) = classificacao;
        Capital = GeoTexto.NormalizarOpcional(capital);
        (Latitude, Longitude, Coordenada) = posicao;
        (CepInicial, CepFinal) = faixaGeral;
        (VersaoDataset, Vigente) = proveniencia;
    }
}
