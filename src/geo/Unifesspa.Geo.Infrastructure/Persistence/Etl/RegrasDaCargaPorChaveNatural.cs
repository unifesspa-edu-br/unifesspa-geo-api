namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Kernel.Domain.Entities;

/// <summary>
/// Regras da carga por chave natural compartilhadas pelos importadores da carga DNE
/// (ADR-0092): a última ocorrência da chave na fonte prevalece, a agregação conta o que a
/// fonte trouxe e as chaves naturais são normalizadas, sem repetir a regra em cada importador.
/// </summary>
internal static class RegrasDaCargaPorChaveNatural
{
    /// <summary>Agrega a fonte por chave (a última ocorrência prevalece) sem contar — para níveis cujos contadores são resolvidos depois.</summary>
    public static async Task<Dictionary<string, T>> AgregarPorChaveAsync<T>(
        IAsyncEnumerable<T> fonte,
        Func<T, string?> chave)
    {
        Dictionary<string, T> mapa = new(StringComparer.Ordinal);
        await foreach (T item in fonte.ConfigureAwait(false))
        {
            string? k = chave(item);
            if (k is not null)
            {
                mapa[k] = item; // a última ocorrência prevalece
            }
        }

        return mapa;
    }

    /// <summary>
    /// Como <see cref="AgregarPorChaveAsync{T}"/>, mas registra no contador da tabela
    /// tudo o que a fonte trouxe (lidos), o que foi descartado por chave ausente e as
    /// chaves repetidas (a última ocorrência prevalece) — para o relatório não subestimar
    /// os "lidos" dos níveis agregados.
    /// </summary>
    public static async Task<Dictionary<string, T>> AgregarContandoAsync<T>(
        IAsyncEnumerable<T> fonte,
        Func<T, string?> chave,
        ContadorTabela contador)
    {
        Dictionary<string, T> mapa = new(StringComparer.Ordinal);
        await foreach (T item in fonte.ConfigureAwait(false))
        {
            contador.ContarLido();
            GuardarUltimaOcorrencia(mapa, chave(item), item, contador);
        }

        return mapa;
    }

    private static void GuardarUltimaOcorrencia<T>(Dictionary<string, T> mapa, string? chave, T item, ContadorTabela contador)
    {
        if (chave is null)
        {
            contador.ContarIgnoradoSemChave();
            return;
        }

        if (!mapa.TryAdd(chave, item))
        {
            contador.ContarDuplicado(chave); // a última ocorrência prevalece
            mapa[chave] = item;
        }
    }

    /// <summary>
    /// Resolve {identificador numérico da fonte → Guid do banco} casando pelo código natural
    /// já resolvido (ex.: <c>id_cidade → cidade_ibge → Cidade.Id</c>) contra um mapa
    /// código→Guid carregado pelo chamador. A fonte DNE não expõe o código natural nas
    /// tabelas de faixa, só o identificador numérico interno (chave primária da fonte).
    /// </summary>
    public static async Task<Dictionary<int, Guid>> ResolverIdsDneParaGuidAsync<T>(
        IAsyncEnumerable<T> fonte,
        Func<T, int?> id,
        Func<T, string?> codigo,
        Dictionary<string, Guid> codigoParaGuid)
    {
        Dictionary<int, Guid> mapa = [];
        await foreach (T item in fonte.ConfigureAwait(false))
        {
            string? chave = ChaveCodigo(codigo(item));
            if (id(item) is int idFonte && chave is not null
                && codigoParaGuid.TryGetValue(chave, out Guid guid))
            {
                mapa[idFonte] = guid; // a última ocorrência prevalece (o identificador da fonte é a chave primária)
            }
        }

        return mapa;
    }

    /// <summary>Chave natural alfabética (sigla/UF): trim + maiúsculas; vazio → <see langword="null"/>.</summary>
    public static string? ChaveSigla(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToUpperInvariant();

    /// <summary>Chave natural numérica/código (código IBGE/CEP): trim; vazio → <see langword="null"/>.</summary>
    public static string? ChaveCodigo(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>Guid da cidade para o identificador da fonte; identificador ausente ou de cidade não carregada vira <see langword="null"/>.</summary>
    public static Guid? ResolverCidade(Dictionary<int, Guid> cidadesPorIdDne, int? cidadeIdDne)
    {
        ArgumentNullException.ThrowIfNull(cidadesPorIdDne);
        return cidadeIdDne is int id && cidadesPorIdDne.TryGetValue(id, out Guid cidadeId) ? cidadeId : null;
    }

    /// <summary>Identificador da fonte como texto para o relatório; ausente vira "(sem id)".</summary>
    public static string IdDaFonteParaRelatorio(int? idDne) =>
        idDne?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(sem id)";

    /// <summary>Chave natural composta de uma faixa de CEP: pai (Guid) + limites — casa com a restrição de unicidade.</summary>
    public static string ChaveFaixa(Guid pai, string cepInicial, string cepFinal) =>
        $"{pai:N}|{cepInicial}|{cepFinal}";

    /// <summary>
    /// Resolve o identificador da fonte de uma localidade opcional (distrito/bairro) para o
    /// Guid do banco, só se ela pertencer à cidade já resolvida; id ausente ou não resolvido
    /// vira <see langword="null"/>. Divergência de cidade degrada para <see langword="null"/>
    /// e é registrada no contador — não vincula registro da cidade A a localidade da cidade B.
    /// </summary>
    public static Guid? ResolverCoerente(
        Dictionary<int, LocalidadeResolvida> mapa,
        int? idDne,
        Guid cidadeGuid,
        ContadorTabela contador)
    {
        ArgumentNullException.ThrowIfNull(mapa);
        ArgumentNullException.ThrowIfNull(contador);

        if (idDne is not int id || !mapa.TryGetValue(id, out LocalidadeResolvida resolvida))
        {
            return null;
        }

        return resolvida.CidadeId == cidadeGuid ? resolvida.Id : DegradarVinculoDeOutraCidade(id, contador);
    }

    private static Guid? DegradarVinculoDeOutraCidade(int id, ContadorTabela contador)
    {
        contador.ContarParseDegradado($"fk_localidade_cidade_divergente:{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return null;
    }

    /// <summary>
    /// Registra que a linha foi revista nesta carga, mesmo sem mudança de valor: força o
    /// <c>updated_at</c> a ser gravado. É o que permite à política de vigência distinguir,
    /// numa recarga da mesma versão, a linha revista da que deixou de ser emitida.
    /// </summary>
    public static void MarcarComoRevista<T>(DbSet<T> dbSet, T entidade)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(dbSet);
        DbContext contexto = ((IInfrastructure<IServiceProvider>)dbSet).Instance
            .GetRequiredService<Microsoft.EntityFrameworkCore.Infrastructure.ICurrentDbContext>().Context;
        MarcarComoRevista(contexto, entidade);
    }

    /// <inheritdoc cref="MarcarComoRevista{T}(DbSet{T}, T)"/>
    public static void MarcarComoRevista(DbContext contexto, object entidade)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Entry(entidade).Property(nameof(EntityBase.UpdatedAt)).IsModified = true;
    }
}
