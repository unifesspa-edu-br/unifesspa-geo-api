namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Executa o encadeamento de carga da versão DNE dentro da transação única do
/// <see cref="GeoDbContext"/>: topo, guarda de versão vazia, folhas, guarda de
/// folhas vazias, marcação de não vigentes e consolidação do relatório.
/// </summary>
internal sealed class ImportadorDaVersaoDne
{
    private const int MaxAmostrasPorTabela = 25;

    private readonly GeoDbContext _contexto;
    private readonly IGeoImportador _importadorTopo;
    private readonly IGeoImportadorLocalidades _importadorFolhas;
    private readonly IGeoFonteDadosFactory _fonteFactory;
    private readonly TimeProvider _relogio;

    public ImportadorDaVersaoDne(
        GeoDbContext contexto,
        IGeoImportador importadorTopo,
        IGeoImportadorLocalidades importadorFolhas,
        IGeoFonteDadosFactory fonteFactory,
        TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(importadorTopo);
        ArgumentNullException.ThrowIfNull(importadorFolhas);
        ArgumentNullException.ThrowIfNull(fonteFactory);
        ArgumentNullException.ThrowIfNull(relogio);

        _contexto = contexto;
        _importadorTopo = importadorTopo;
        _importadorFolhas = importadorFolhas;
        _fonteFactory = fonteFactory;
        _relogio = relogio;
    }

    public async Task<ResultadoDaVersao> ImportarAsync(string versao, DateTimeOffset inicioCarga, CancellationToken cancellationToken)
    {
        IGeoFonteDados fonte = _fonteFactory.Criar(versao);
        RelatorioImportacao relTopo = await _importadorTopo.ImportarAsync(fonte, cancellationToken).ConfigureAwait(false);
        ContadorTabela cidade = relTopo.Tabela("cidade");
        if (cidade.Inseridos + cidade.Atualizados == 0)
        {
            return new ResultadoDaVersao(MotivoRecusaDaVersao.VersaoVazia, null);
        }

        return await ImportarFolhasEMarcarAsync(fonte, versao, inicioCarga, relTopo, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResultadoDaVersao> ImportarFolhasEMarcarAsync(
        IGeoFonteDados fonte,
        string versao,
        DateTimeOffset inicioCarga,
        RelatorioImportacao relTopo,
        CancellationToken cancellationToken)
    {
        RelatorioImportacao relFolhas = await _importadorFolhas.ImportarAsync(fonte, cancellationToken).ConfigureAwait(false);
        ContadorTabela logradouro = relFolhas.Tabela("logradouro");
        if (logradouro.Inseridos + logradouro.Atualizados == 0)
        {
            return new ResultadoDaVersao(MotivoRecusaDaVersao.FolhasVazias, null);
        }

        await MarcacaoDeNaoVigentes
            .MarcarAsync(_contexto, versao, inicioCarga, _relogio.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return new ResultadoDaVersao(MotivoRecusaDaVersao.Nenhum, CombinarRelatorios(versao, relTopo, relFolhas));
    }

    private static RelatorioImportacaoDto CombinarRelatorios(string versao, params RelatorioImportacao[] relatorios)
    {
        Dictionary<string, TabelaImportacaoDto> mapa = new(StringComparer.Ordinal);
        foreach (RelatorioImportacao relatorio in relatorios)
        {
            AcumularTabelas(mapa, relatorio);
        }

        List<TabelaImportacaoDto> tabelas = [.. mapa.Values.OrderBy(t => t.Tabela, StringComparer.Ordinal)];
        return MontarResumoDto(versao, tabelas);
    }

    private static void AcumularTabelas(Dictionary<string, TabelaImportacaoDto> mapa, RelatorioImportacao relatorio)
    {
        foreach ((string nome, ContadorTabela contador) in relatorio.Tabelas)
        {
            mapa[nome] = mapa.TryGetValue(nome, out TabelaImportacaoDto? existente)
                ? Somar(existente, contador)
                : ParaDto(nome, contador);
        }
    }

    private static RelatorioImportacaoDto MontarResumoDto(string versao, List<TabelaImportacaoDto> tabelas) =>
        new(
            versao,
            tabelas.Sum(t => t.Lidos),
            tabelas.Sum(t => t.Inseridos),
            tabelas.Sum(t => t.Atualizados),
            tabelas.Sum(t => t.Orfaos),
            tabelas.Sum(t => t.ParsesDegradados),
            tabelas);

    private static TabelaImportacaoDto ParaDto(string nome, ContadorTabela c) =>
        new(nome, c.Lidos, c.Inseridos, c.Atualizados, c.IgnoradosSemChave, c.Orfaos, c.Duplicados, c.ParsesDegradados,
            [.. c.Amostras.Take(MaxAmostrasPorTabela)]);

    private static TabelaImportacaoDto Somar(TabelaImportacaoDto a, ContadorTabela c) =>
        new(
            a.Tabela,
            a.Lidos + c.Lidos,
            a.Inseridos + c.Inseridos,
            a.Atualizados + c.Atualizados,
            a.IgnoradosSemChave + c.IgnoradosSemChave,
            a.Orfaos + c.Orfaos,
            a.Duplicados + c.Duplicados,
            a.ParsesDegradados + c.ParsesDegradados,
            MesclarAmostras(a.Amostras, c.Amostras));

    private static List<string> MesclarAmostras(IReadOnlyList<string> existentes, IReadOnlyList<string> novas)
    {
        List<string> amostras = [.. existentes];
        foreach (string amostra in novas)
        {
            if (amostras.Count >= MaxAmostrasPorTabela)
            {
                break;
            }

            amostras.Add(amostra);
        }

        return amostras;
    }
}

internal enum MotivoRecusaDaVersao
{
    Nenhum,
    VersaoVazia,
    FolhasVazias,
}

internal sealed record ResultadoDaVersao(MotivoRecusaDaVersao MotivoRecusa, RelatorioImportacaoDto? Relatorio);
