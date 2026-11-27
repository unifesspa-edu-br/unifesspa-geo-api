namespace Unifesspa.Geo.Infrastructure.Persistence.Readers;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Cep;

/// <summary>
/// Resolve um CEP pela cascata determinística (ADR-0090) sobre o
/// <see cref="GeoDbContext"/>, só com reference data vigente (ADR-0092):
/// <list type="number">
/// <item><c>Logradouro</c> pelo CEP — primário (desempate estável) + alternativos;</item>
/// <item><c>CepGrandeUsuario</c> — nome do órgão + cidade/UF da faixa CEP;</item>
/// <item><c>CepGeral</c> — CEP oficial da cidade ou do distrito (resolução exata);</item>
/// <item>faixa de cidade (e bairro/distrito mais específicos) que contém o CEP;</item>
/// <item>nada casa → <see langword="null"/> (404 no controller).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><strong>Invariante 8 dígitos zero-padded:</strong> o predicado de range
/// das faixas (<c>cep_inicial &lt;= @cep AND cep_final &gt;= @cep</c>) usa a comparação
/// lexicográfica de string nativa do Postgres. Ela só equivale à comparação
/// <em>numérica</em> de CEP porque os três lados (o CEP de entrada e os limites das
/// faixas) são <strong>exatamente 8 dígitos com zero à esquerda</strong>. Para o CEP de
/// entrada, isso é garantido por <c>CepValido</c> no boundary (ADR-0031); para os limites,
/// pela carga do ETL (ADR-0092). Um CEP de comprimento diferente quebraria a ordenação
/// lexicográfica (ex.: <c>"9" &gt; "10"</c>) — por isso o boundary é a única porta.</para>
/// <para><strong>Dependência de nomes físicos:</strong> os <c>FromSqlInterpolated</c> das
/// faixas fixam os nomes físicos de tabela/coluna (<c>cidade_faixa_cep</c>,
/// <c>cep_inicial</c>, ...) em SQL cru — fora do alcance do mapeamento EF. Um rename de
/// tabela/coluna no mapeamento <strong>quebra em runtime</strong>, sem aviso de
/// compilação. Ao renomear, atualizar também estes literais.</para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via DI em GeoInfrastructureRegistration.")]
internal sealed partial class CepReader : ICepReader
{
    private readonly GeoDbContext _dbContext;
    private readonly int _tetoLogradouros;
    private readonly ILogger<CepReader> _logger;

    public CepReader(GeoDbContext dbContext, IOptions<GeoCepLookupOptions> opcoes, ILogger<CepReader> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(logger);
        _dbContext = dbContext;
        // Teto < 1 esvaziaria o lookup (Take(0) → null para CEP válido); clamp defensivo.
        _tetoLogradouros = Math.Max(1, opcoes.Value.MaxLogradourosPorCep);
        _logger = logger;
    }

    public async Task<CepResolvidoDto?> ResolverAsync(string cep, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cep);

        return await ResolverPorLogradouroAsync(cep, cancellationToken).ConfigureAwait(false)
            ?? await ResolverPorGrandeUsuarioAsync(cep, cancellationToken).ConfigureAwait(false)
            ?? await ResolverPorCepGeralAsync(cep, cancellationToken).ConfigureAwait(false)
            ?? await ResolverPorFaixaAsync(cep, cancellationToken).ConfigureAwait(false);
    }

    // (1) Logradouro pelo CEP. CEP não é único: retorna os logradouros ordenados pela
    // chave de desempate estável (nome_normalizado, distrito_id, bairro_id, Id) — o Id
    // (Guid v7) é o tie-breaker final, garantindo o mesmo primário/ordem entre execuções.
    private async Task<CepResolvidoDto?> ResolverPorLogradouroAsync(string cep, CancellationToken cancellationToken)
    {
        List<CepLogradouroLinha> linhas = await LogradourosDoCepAteOTetoAsync(cep, cancellationToken).ConfigureAwait(false);

        if (linhas.Count == 0)
        {
            return null;
        }

        string? complemento = await ComplementoUnicoDoCepAsync(cep, cancellationToken).ConfigureAwait(false);

        return linhas[0].ComoPrimario(cep, complemento) with
        {
            Alternativos = [.. linhas.Skip(1).Select(static l => l.ComoAlternativo())],
        };
    }

    // A materialização é limitada ao teto defensivo: um CEP patológico (anomalia da fonte)
    // não materializa milhares de linhas num endpoint de consulta. Lê uma linha além do teto
    // para distinguir truncamento real de cardinalidade igual ao teto.
    private async Task<List<CepLogradouroLinha>> LogradourosDoCepAteOTetoAsync(string cep, CancellationToken cancellationToken)
    {
        List<CepLogradouroLinha> linhas = await LogradourosVigentesDoCep(cep)
            .Take(_tetoLogradouros + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Excedeu o teto → trunca o extra e sinaliza (não mascara a anomalia).
        if (linhas.Count > _tetoLogradouros)
        {
            linhas.RemoveAt(linhas.Count - 1);
            LogFanOutTruncado(_logger, cep, _tetoLogradouros);
        }

        return linhas;
    }

    private IQueryable<CepLogradouroLinha> LogradourosVigentesDoCep(string cep) =>
        from l in _dbContext.Logradouros.AsNoTracking()
        where l.Vigente && l.Cep == cep
        join c in _dbContext.Cidades.AsNoTracking().Where(c => c.Vigente) on l.CidadeId equals c.Id
        from d in _dbContext.Distritos.Where(d => d.Vigente && d.Id == l.DistritoId).DefaultIfEmpty()
        from b in _dbContext.Bairros.Where(b => b.Vigente && b.Id == l.BairroId).DefaultIfEmpty()
        orderby l.NomeNormalizado, l.DistritoId, l.BairroId, l.Id
        select new CepLogradouroLinha(
            l.Tipo,
            l.Nome,
            l.Latitude,
            l.Longitude,
            new CidadeDoEndereco(c.CodigoIbge, c.Nome, c.Uf),
            d == null ? null : d.Nome,
            b == null ? null : b.Nome);

    // Complemento (lado par/ímpar, faixa) é atributo do CEP, sem FK ao logradouro e
    // potencialmente múltiplo. Só preenche quando há exatamente UM — escolha
    // determinística e auditável; ambíguo (0 ou >1) fica null.
    private async Task<string?> ComplementoUnicoDoCepAsync(string cep, CancellationToken cancellationToken)
    {
        List<string> complementos = await _dbContext.LogradouroComplementos
            .AsNoTracking()
            .Where(lc => lc.Vigente && lc.Cep == cep)
            .Select(lc => lc.Complemento)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return complementos.Count == 1 ? complementos[0] : null;
    }

    // (2) CepGrandeUsuario (CEP exclusivo de órgão). O grande usuário só guarda
    // cep+nome — cidade/UF vêm da faixa de cidade que contém o CEP. O nome enriquece
    // o campo Logradouro (o DTO não tem campo dedicado). Sem faixa, não há como
    // territorializar → null (não resolve).
    private async Task<CepResolvidoDto?> ResolverPorGrandeUsuarioAsync(string cep, CancellationToken cancellationToken)
    {
        if (await NomeDoGrandeUsuarioVigenteAsync(cep, cancellationToken).ConfigureAwait(false) is not { } nome)
        {
            return null;
        }

        CepTerritorioCidade? cidade = await ResolverCidadePorFaixaAsync(cep, cancellationToken).ConfigureAwait(false);

        return cidade?.ComoCepResolvido(
            cep,
            logradouro: nome,
            bairro: null,
            distrito: null,
            CepResolucao.NivelCidade,
            CepResolucao.OrigemGrandeUsuario);
    }

    private Task<string?> NomeDoGrandeUsuarioVigenteAsync(string cep, CancellationToken cancellationToken) =>
        _dbContext.CepGrandesUsuarios
            .AsNoTracking()
            .Where(g => g.Vigente && g.Cep == cep)
            .Select(g => g.Nome)
            .FirstOrDefaultAsync(cancellationToken);

    // (3) CEP geral oficial de cidade/distrito: resolução exata (não inferida por faixa).
    // Nível distrito quando o registro traz distrito vigente; senão, cidade. A coordenada
    // é a do próprio CEP geral; quando ele não a traz, usa a da cidade.
    private async Task<CepResolvidoDto?> ResolverPorCepGeralAsync(string cep, CancellationToken cancellationToken)
    {
        CepGeralLinha? linha = await CepGeralVigente(cep)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return linha?.ComoCepResolvido(cep);
    }

    private IQueryable<CepGeralLinha> CepGeralVigente(string cep) =>
        from g in _dbContext.CepGerais.AsNoTracking()
        where g.Vigente && g.Cep == cep
        join c in _dbContext.Cidades.AsNoTracking().Where(c => c.Vigente) on g.CidadeId equals c.Id
        select new CepGeralLinha(
            new CidadeDoEndereco(c.CodigoIbge, c.Nome, c.Uf),
            g.Latitude ?? c.Latitude,
            g.Longitude ?? c.Longitude,
            _dbContext.Distritos
                .Where(d => d.Vigente && d.Id == g.DistritoId)
                .Select(d => (string?)d.Nome)
                .FirstOrDefault());

    // (4) Faixa: cidade que contém o CEP; enriquece com bairro/distrito quando houver
    // faixa mais específica. Nível/origem refletem o mais específico (bairro > distrito
    // > cidade). Logradouro ausente é esperado (CEP geral), não erro.
    private async Task<CepResolvidoDto?> ResolverPorFaixaAsync(string cep, CancellationToken cancellationToken)
    {
        CepTerritorioCidade? cidade = await ResolverCidadePorFaixaAsync(cep, cancellationToken).ConfigureAwait(false);
        if (cidade is null)
        {
            return null;
        }

        string? bairroNome = await ResolverNomePorFaixaBairroAsync(cep, cidade.Id, cancellationToken).ConfigureAwait(false);
        string? distritoNome = await ResolverNomePorFaixaDistritoAsync(cep, cidade.Id, cancellationToken).ConfigureAwait(false);
        (string nivel, string origem) = NivelEOrigemDaFaixa(bairroNome, distritoNome);

        return cidade.ComoCepResolvido(cep, logradouro: null, bairroNome, distritoNome, nivel, origem);
    }

    private static (string Nivel, string Origem) NivelEOrigemDaFaixa(string? bairroNome, string? distritoNome) =>
        (bairroNome, distritoNome) switch
        {
            (not null, _) => (CepResolucao.NivelBairro, CepResolucao.OrigemFaixaBairro),
            (null, not null) => (CepResolucao.NivelDistrito, CepResolucao.OrigemFaixaDistrito),
            _ => (CepResolucao.NivelCidade, CepResolucao.OrigemFaixaCidade),
        };

    // Faixa de CEP da cidade que contém o CEP. O predicado de range
    // (cep_inicial <= cep <= cep_final) vai em SQL parametrizado (FromSqlInterpolated):
    // comparação de string nativa do PG sobre 8 dígitos zero-padded — sargável e sem
    // depender da tradução de string.Compare. OrderBy por Id torna a escolha
    // determinística mesmo se faixas se sobrepuserem (dado inconsistente da fonte).
    private Task<CepTerritorioCidade?> ResolverCidadePorFaixaAsync(string cep, CancellationToken cancellationToken)
    {
        FormattableString faixaSql =
            $"SELECT * FROM cidade_faixa_cep WHERE vigente AND cep_inicial <= {cep} AND cep_final >= {cep}";

        return (
            from f in _dbContext.CidadeFaixasCep.FromSqlInterpolated(faixaSql).AsNoTracking()
            join c in _dbContext.Cidades.AsNoTracking().Where(c => c.Vigente) on f.CidadeId equals c.Id
            orderby c.Id
            select new CepTerritorioCidade(c.Id, new CidadeDoEndereco(c.CodigoIbge, c.Nome, c.Uf), c.Latitude, c.Longitude))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Faixa de bairro restrita à cidade já resolvida (a faixa indexa BairroId, mas o
    // bairro carrega CidadeId) + desempate estável — range sobreposto não devolve
    // bairro de outra cidade nem varia entre execuções.
    private Task<string?> ResolverNomePorFaixaBairroAsync(string cep, Guid cidadeId, CancellationToken cancellationToken)
    {
        FormattableString faixaSql =
            $"SELECT * FROM bairro_faixa_cep WHERE vigente AND cep_inicial <= {cep} AND cep_final >= {cep}";

        return (
            from f in _dbContext.BairroFaixasCep.FromSqlInterpolated(faixaSql).AsNoTracking()
            join b in _dbContext.Bairros.AsNoTracking().Where(b => b.Vigente && b.CidadeId == cidadeId) on f.BairroId equals b.Id
            orderby b.NomeNormalizado, b.Id
            select (string?)b.Nome)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<string?> ResolverNomePorFaixaDistritoAsync(string cep, Guid cidadeId, CancellationToken cancellationToken)
    {
        FormattableString faixaSql =
            $"SELECT * FROM distrito_faixa_cep WHERE vigente AND cep_inicial <= {cep} AND cep_final >= {cep}";

        return (
            from f in _dbContext.DistritoFaixasCep.FromSqlInterpolated(faixaSql).AsNoTracking()
            join d in _dbContext.Distritos.AsNoTracking().Where(d => d.Vigente && d.CidadeId == cidadeId) on f.DistritoId equals d.Id
            orderby d.NomeNormalizado, d.Id
            select (string?)d.Nome)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record CidadeDoEndereco(string CodigoIbge, string Nome, string Uf);

    // Projeções read-only (evitam materializar a entidade inteira).
    private sealed record CepLogradouroLinha(
        string? Tipo,
        string Nome,
        decimal? Latitude,
        decimal? Longitude,
        CidadeDoEndereco Cidade,
        string? DistritoNome,
        string? BairroNome)
    {
        public CepResolvidoDto ComoPrimario(string cep, string? complemento)
        {
            return new CepResolvidoDto(
                Cep: cep,
                Tipo: Tipo,
                Logradouro: Nome,
                Complemento: complemento,
                Bairro: BairroNome,
                Distrito: DistritoNome,
                Cidade: Cidade.Nome,
                CodigoIbge: Cidade.CodigoIbge,
                Uf: Cidade.Uf,
                Latitude: Latitude,
                Longitude: Longitude,
                NivelResolucao: CepResolucao.NivelLogradouro,
                Origem: CepResolucao.OrigemLogradouro);
        }

        public LogradouroAlternativoDto ComoAlternativo() =>
            new(Tipo, Nome, BairroNome, DistritoNome, Latitude, Longitude);
    }

    private sealed record CepGeralLinha(
        CidadeDoEndereco Cidade,
        decimal? Latitude,
        decimal? Longitude,
        string? DistritoNome)
    {
        public CepResolvidoDto ComoCepResolvido(string cep)
        {
            return new CepResolvidoDto(
                Cep: cep,
                Tipo: null,
                Logradouro: null,
                Complemento: null,
                Bairro: null,
                Distrito: DistritoNome,
                Cidade: Cidade.Nome,
                CodigoIbge: Cidade.CodigoIbge,
                Uf: Cidade.Uf,
                Latitude: Latitude,
                Longitude: Longitude,
                NivelResolucao: DistritoNome is null ? CepResolucao.NivelCidade : CepResolucao.NivelDistrito,
                Origem: CepResolucao.OrigemCepGeral);
        }
    }

    private sealed record CepTerritorioCidade(
        Guid Id,
        CidadeDoEndereco Cidade,
        decimal? Latitude,
        decimal? Longitude)
    {
        public CepResolvidoDto ComoCepResolvido(
            string cep,
            string? logradouro,
            string? bairro,
            string? distrito,
            string nivel,
            string origem)
        {
            return new CepResolvidoDto(
                Cep: cep,
                Tipo: null,
                Logradouro: logradouro,
                Complemento: null,
                Bairro: bairro,
                Distrito: distrito,
                Cidade: Cidade.Nome,
                CodigoIbge: Cidade.CodigoIbge,
                Uf: Cidade.Uf,
                Latitude: Latitude,
                Longitude: Longitude,
                NivelResolucao: nivel,
                Origem: origem);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Geo: lookup de CEP {Cep} truncou o fan-out de logradouros no teto de {Teto} — possível anomalia da fonte DNE.")]
    private static partial void LogFanOutTruncado(ILogger logger, string cep, int teto);
}
