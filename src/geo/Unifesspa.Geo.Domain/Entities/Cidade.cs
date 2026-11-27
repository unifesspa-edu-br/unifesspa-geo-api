namespace Unifesspa.Geo.Domain.Entities;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Município — eixo central do Geo, referenciado por outros módulos por
/// <see cref="CodigoIbge"/> (usado por Campus e LocalOferta). Reference data sem
/// soft-delete (ADR-0092). Os campos <strong>territoriais</strong> (meso/micro/
/// região intermediária e imediata, código + nome) ficam embutidos aqui (identidade
/// geográfica); os socioeconômicos vão no satélite <see cref="CidadeIndicador"/>.
/// </summary>
/// <remarks>
/// Chave natural <see cref="CodigoIbge"/> (7 dígitos, UNIQUE) — homônimos em UFs
/// distintas coexistem (a chave é o código, não o nome). <see cref="NomeNormalizado"/>
/// guarda o nome do município sem acento e sem apóstrofo, e ganha índice trigram para o
/// autocomplete acento-insensível — não compõe a chave (fica nullable).
/// </remarks>
public sealed class Cidade : EntityBase
{
    /// <summary>FK intra-banco para o <see cref="Estado"/> (ADR-0054).</summary>
    public Guid EstadoId { get; private set; }

    /// <summary>UF do município (denormalizada da fonte; o vínculo forte é <see cref="EstadoId"/>).</summary>
    public string Uf { get; private set; } = string.Empty;

    /// <summary>Código IBGE de 7 dígitos (chave natural, UNIQUE).</summary>
    public string CodigoIbge { get; private set; } = string.Empty;

    public string Nome { get; private set; } = string.Empty;

    /// <summary>Nome sem acentos e sem apóstrofo (gerado na carga a partir do nome oficial) — base do autocomplete.</summary>
    public string? NomeNormalizado { get; private set; }

    /// <summary>
    /// Chave técnica gerada pelo banco para ordenação alfabética server-side. Usa
    /// <see cref="NomeNormalizado"/> quando presente e fallback normalizado de
    /// <see cref="Nome"/> quando a fonte não trouxe a coluna de busca.
    /// </summary>
    public string NomeOrdenacao { get; private set; } = string.Empty;

    public string? Ddd { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    /// <summary>Coordenada geográfica (SRID 4326) — <c>geography(Point,4326)</c> (ADR-0091).</summary>
    public Point? Coordenada { get; private set; }

    public string? MesorregiaoCodigo { get; private set; }

    public string? MesorregiaoNome { get; private set; }

    public string? MicrorregiaoCodigo { get; private set; }

    public string? MicrorregiaoNome { get; private set; }

    public string? RegiaoIntermediariaCodigo { get; private set; }

    public string? RegiaoIntermediariaNome { get; private set; }

    public string? RegiaoImediataCodigo { get; private set; }

    public string? RegiaoImediataNome { get; private set; }

    /// <summary>Versão da base dos Correios (AAAAMM) de origem — proveniência da carga (ADR-0092).</summary>
    public string VersaoDataset { get; private set; } = string.Empty;

    /// <summary><see langword="false"/> quando o código IBGE some da versão vigente (registro não vigente).</summary>
    public bool Vigente { get; private set; } = true;

    // Construtor privado para materialização do EF Core.
    private Cidade()
    {
    }

    /// <summary>
    /// Importa um Município a partir de valores já tipados (leitura tolerante na carga).
    /// Valida só o mínimo: o estado em <paramref name="estado"/>, a chave natural
    /// (<paramref name="codigoIbge"/>), o nome em <paramref name="nome"/> e a versão da base em
    /// <paramref name="proveniencia"/>.
    /// </summary>
    public static Result<Cidade> Importar(
        string codigoIbge,
        EstadoDaCidade estado,
        NomeComFormaDeBusca nome,
        string? ddd,
        PosicaoGeografica posicao,
        DivisaoRegionalDaCidade divisao,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(codigoIbge);
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(estado.EstadoId, codigoIbge, nome.Nome, proveniencia);
        if (erro is not null)
        {
            return Result<Cidade>.Failure(erro);
        }

        var cidade = new Cidade { CodigoIbge = codigoIbge.Trim() };
        cidade.Aplicar(estado, nome, ddd, posicao, divisao, proveniencia);

        return Result<Cidade>.Success(cidade);
    }

    /// <summary>
    /// Reaplica os dados de uma versão da base sobre um Município já existente (atualização
    /// pela chave natural da carga), preservando <see cref="EntityBase.Id"/> (referenciado por
    /// Indicador/faixas e por outros módulos via código IBGE) e a chave natural
    /// <see cref="CodigoIbge"/>. Os campos territoriais são reaplicados juntos (a fonte os agrega
    /// por código IBGE antes da chamada). Valida o mínimo <strong>antes</strong> de mutar — em
    /// falha, o estado não é tocado.
    /// </summary>
    public Result Atualizar(
        EstadoDaCidade estado,
        NomeComFormaDeBusca nome,
        string? ddd,
        PosicaoGeografica posicao,
        DivisaoRegionalDaCidade divisao,
        ProvenienciaDaCarga proveniencia)
    {
        ArgumentNullException.ThrowIfNull(proveniencia);

        DomainError? erro = Validar(estado.EstadoId, CodigoIbge, nome.Nome, proveniencia);
        if (erro is not null)
        {
            return Result.Failure(erro);
        }

        Aplicar(estado, nome, ddd, posicao, divisao, proveniencia);

        return Result.Success();
    }

    private static DomainError? Validar(Guid estadoId, string codigoIbge, string nome, ProvenienciaDaCarga proveniencia) =>
        estadoId == Guid.Empty
            ? new DomainError(GeoReferenceDataErrorCodes.CidadeEstadoObrigatorio, "Estado da Cidade é obrigatório.")
            : string.IsNullOrWhiteSpace(codigoIbge)
                ? new DomainError(GeoReferenceDataErrorCodes.CidadeCodigoIbgeObrigatorio, "Código IBGE da Cidade é obrigatório.")
                : string.IsNullOrWhiteSpace(nome)
                    ? new DomainError(GeoReferenceDataErrorCodes.CidadeNomeObrigatorio, "Nome da Cidade é obrigatório.")
                    : proveniencia.TemVersao
                        ? null
                        : new DomainError(
                            GeoReferenceDataErrorCodes.CidadeVersaoDatasetObrigatoria,
                            "Versão do dataset (proveniência) da Cidade é obrigatória.");

    private void Aplicar(
        EstadoDaCidade estado,
        NomeComFormaDeBusca nome,
        string? ddd,
        PosicaoGeografica posicao,
        DivisaoRegionalDaCidade divisao,
        ProvenienciaDaCarga proveniencia)
    {
        (EstadoId, Uf) = estado;
        (Nome, NomeNormalizado) = nome;
        Ddd = GeoTexto.NormalizarOpcional(ddd);
        (Latitude, Longitude, Coordenada) = posicao;
        (MesorregiaoCodigo, MesorregiaoNome) = divisao.Mesorregiao;
        (MicrorregiaoCodigo, MicrorregiaoNome) = divisao.Microrregiao;
        (RegiaoIntermediariaCodigo, RegiaoIntermediariaNome) = divisao.RegiaoIntermediaria;
        (RegiaoImediataCodigo, RegiaoImediataNome) = divisao.RegiaoImediata;
        (VersaoDataset, Vigente) = proveniencia;
    }
}
