namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Lê, em fluxo e sem executar nada, um arquivo <c>tbl_cep_{versao}_n_{tabela}.sql</c> do pacote
/// dos Correios, pela gramática fechada: <c>DROP TABLE</c>, <c>CREATE TABLE</c>, <c>INSERT</c>,
/// <c>CREATE INDEX</c>, <c>ALTER TABLE ... PRIMARY KEY</c> e comentários. Confere as colunas
/// obrigatórias contra <see cref="TabelasDne"/>. Toda recusa vira
/// <see cref="ErroDeLeituraDoPacoteDneException"/>, com tabela e linha, sem conteúdo.
/// </summary>
internal sealed class LeitorDoArquivoSqlDne
{
    private const string InicioDeComentarioDeBloco = "/*";
    private const string FimDeComentarioDeBloco = "*/";
    private const string FechamentoEmUmaLinha = ");";

    private readonly LinhasDoArquivoSql _linhas;
    private readonly string _tabela;
    private readonly string _versao;
    private readonly string _inicioDoInsert;
    private readonly ColunasDaTabelaLida _colunas = new();
    private EstadoDoArquivoSql _estado = EstadoDoArquivoSql.AntesDaTabela;
    private EstadoDoArquivoSql _estadoAoSairDoComentario;
    private bool _tabelaFoiApagada;
    private int _colunasDoIndice;

    public LeitorDoArquivoSqlDne(Stream fluxo, string tabela, string versao, long limiteDeBytes)
    {
        ArgumentNullException.ThrowIfNull(fluxo);
        ArgumentOutOfRangeException.ThrowIfNegative(limiteDeBytes);
        if (!TabelasDne.Contem(tabela) || !GeoImportacaoExecucao.EhVersaoValida(versao))
        {
            throw new ArgumentException("Tabela ou versão fora do pacote dos Correios.", nameof(tabela));
        }

        _tabela = tabela;
        _versao = versao;
        _inicioDoInsert = $"INSERT INTO \"public\".\"{NomeDaTabelaDne.Base(versao, tabela)}\" VALUES (";
        _linhas = new LinhasDoArquivoSql(fluxo, tabela, limiteDeBytes);
    }

    /// <summary>Bytes descompactados lidos do arquivo até aqui.</summary>
    public long BytesDescompactados => _linhas.BytesLidos;

    /// <summary>
    /// Lê até o fim do <c>CREATE TABLE</c> e devolve as colunas obrigatórias da tabela, na ordem em
    /// que <see cref="LerLinhasAsync"/> entrega os valores. As colunas extras do arquivo são descartadas.
    /// </summary>
    public async Task<IReadOnlyList<ColunaDne>> LerCabecalhoAsync(CancellationToken cancellationToken)
    {
        while (_estado != EstadoDoArquivoSql.AposCreateTable)
        {
            string linha = await _linhas.LerLinhaAsync(cancellationToken).ConfigureAwait(false)
                ?? throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ArquivoIncompleto);
            Processar(linha);
        }

        return TabelasDne.ColunasObrigatorias(_tabela);
    }

    /// <summary>
    /// Lê o resto do arquivo, devolvendo os valores das colunas obrigatórias de cada <c>INSERT</c>
    /// (na ordem de <see cref="LerCabecalhoAsync"/>), e confere o fim do arquivo.
    /// </summary>
    public async IAsyncEnumerable<object?[]> LerLinhasAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? linha;
        while ((linha = await _linhas.LerLinhaAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            object?[]? valores = Processar(linha);
            if (valores is not null)
            {
                yield return valores;
            }
        }

        ConferirFimDoArquivo();
    }

    private void ConferirFimDoArquivo()
    {
        if (_estado != EstadoDoArquivoSql.AposCreateTable)
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ArquivoIncompleto);
        }
    }

    private object?[]? Processar(string linha)
    {
        switch (_estado)
        {
            case EstadoDoArquivoSql.AntesDaTabela: ProcessarAntesDaTabela(linha); return null;
            case EstadoDoArquivoSql.EmComentarioDeBloco: FecharComentarioSeTerminar(linha, 0); return null;
            case EstadoDoArquivoSql.EmCreateTable: ProcessarColuna(linha); return null;
            case EstadoDoArquivoSql.AguardandoPontoEVirgulaCreateTable: ProcessarPontoEVirgula(linha); return null;
            case EstadoDoArquivoSql.EmCreateIndex: ProcessarLinhaDoIndice(linha); return null;
            default: return ProcessarAposCreateTable(linha);
        }
    }

    private void ProcessarAntesDaTabela(string linha)
    {
        if (EhBrancoOuComentarioDeLinha(linha)
            || TentarIniciarComentarioDeBloco(linha)
            || TentarApagarTabela(linha)
            || TentarAbrirCriacaoDaTabela(linha))
        {
            return;
        }

        throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ComandoForaDaGramatica);
    }

    private object?[]? ProcessarAposCreateTable(string linha)
    {
        if (linha.StartsWith("INSERT ", StringComparison.Ordinal))
        {
            return LerInsert(linha);
        }

        if (!EhBrancoOuComentarioDeLinha(linha) && !TentarIniciarComentarioDeBloco(linha)
            && !TentarAbrirIndice(linha) && !TentarChavePrimaria(linha))
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ComandoForaDaGramatica);
        }

        return null;
    }

    private static bool EhBrancoOuComentarioDeLinha(string linha) =>
        string.IsNullOrWhiteSpace(linha) || linha.AsSpan().TrimStart().StartsWith("--", StringComparison.Ordinal);

    private bool TentarIniciarComentarioDeBloco(string linha)
    {
        string aparada = linha.TrimStart();
        if (!aparada.StartsWith(InicioDeComentarioDeBloco, StringComparison.Ordinal))
        {
            return false;
        }

        _estadoAoSairDoComentario = _estado;
        _estado = EstadoDoArquivoSql.EmComentarioDeBloco;
        FecharComentarioSeTerminar(aparada, InicioDeComentarioDeBloco.Length);
        return true;
    }

    private void FecharComentarioSeTerminar(string linha, int inicioDaBusca)
    {
        int fim = linha.IndexOf(FimDeComentarioDeBloco, inicioDaBusca, StringComparison.Ordinal);
        if (fim < 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(linha[(fim + FimDeComentarioDeBloco.Length)..]))
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.CodigoAposComentario);
        }

        _estado = _estadoAoSairDoComentario;
    }

    private bool TentarApagarTabela(string linha)
    {
        Match comando = PadroesDoArquivoSqlDne.ApagaTabela().Match(linha);
        if (!comando.Success)
        {
            return false;
        }

        if (_tabelaFoiApagada)
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ComandoForaDaGramatica);
        }

        ConferirNome(comando);
        _tabelaFoiApagada = true;
        return true;
    }

    private bool TentarAbrirCriacaoDaTabela(string linha)
    {
        Match comando = PadroesDoArquivoSqlDne.AbreCriacaoDaTabela().Match(linha);
        if (!comando.Success)
        {
            return false;
        }

        ConferirNome(comando);
        _estado = EstadoDoArquivoSql.EmCreateTable;
        return true;
    }

    private void ConferirNome(Match comando)
    {
        if (comando.Groups["versao"].Value != _versao || comando.Groups["tabela"].Value != _tabela)
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.NomeDivergente);
        }
    }

    private void ProcessarColuna(string linha)
    {
        if (!TentarFecharColunas(linha))
        {
            AdicionarColuna(linha);
        }
    }

    private bool TentarFecharColunas(string linha)
    {
        if (linha == FechamentoEmUmaLinha)
        {
            FecharCriacaoDaTabela();
            return true;
        }

        if (linha != ")")
        {
            return false;
        }

        _estado = EstadoDoArquivoSql.AguardandoPontoEVirgulaCreateTable;
        return true;
    }

    private void AdicionarColuna(string linha)
    {
        Match coluna = PadroesDoArquivoSqlDne.ColunaDaTabela().Match(linha);
        if (!coluna.Success || !_colunas.Adicionar(ColunaDe(coluna)))
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ColunaInvalida);
        }
    }

    private static ColunaDne ColunaDe(Match coluna) =>
        new(coluna.Groups["nome"].Value, coluna.Groups["inteiro"].Success ? TipoDeColunaDne.Inteiro : TipoDeColunaDne.Texto);

    private void ProcessarPontoEVirgula(string linha)
    {
        if (linha != ";")
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ComandoForaDaGramatica);
        }

        FecharCriacaoDaTabela();
    }

    private void FecharCriacaoDaTabela()
    {
        if (!_colunas.AtendeObrigatorias(_tabela))
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ColunasObrigatorias);
        }

        _estado = EstadoDoArquivoSql.AposCreateTable;
    }

    private object?[] LerInsert(string linha)
    {
        object?[] valores = LerValoresDoInsert(linha);
        return _colunas.AceitaValores(valores)
            ? _colunas.ValoresObrigatorios(_tabela, valores)
            : throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ValoresIncompativeis);
    }

    private object?[] LerValoresDoInsert(string linha)
    {
        ConferirFormaDoInsert(linha);
        ReadOnlySpan<char> tupla = linha.AsSpan(_inicioDoInsert.Length, linha.Length - _inicioDoInsert.Length - FechamentoEmUmaLinha.Length);
        return ValoresDoInsert.TentarLer(tupla, out object?[] valores)
            ? valores
            : throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ValoresForaDaGramatica);
    }

    private void ConferirFormaDoInsert(string linha)
    {
        if (!linha.StartsWith(_inicioDoInsert, StringComparison.Ordinal)
            || !linha.EndsWith(FechamentoEmUmaLinha, StringComparison.Ordinal)
            || linha.Length < _inicioDoInsert.Length + FechamentoEmUmaLinha.Length)
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ComandoForaDaGramatica);
        }
    }

    private bool TentarAbrirIndice(string linha)
    {
        Match comando = PadroesDoArquivoSqlDne.AbreIndice().Match(linha);
        if (!comando.Success)
        {
            return false;
        }

        ConferirNome(comando);
        IniciarIndice(comando.Groups["colunas"]);
        return true;
    }

    private void IniciarIndice(Group colunasEmLinhaUnica)
    {
        if (colunasEmLinhaUnica.Success)
        {
            ConferirColunasDoIndice(colunasEmLinhaUnica.Value.Split(','));
            return;
        }

        _colunasDoIndice = 0;
        _estado = EstadoDoArquivoSql.EmCreateIndex;
    }

    private void ProcessarLinhaDoIndice(string linha)
    {
        if (linha == FechamentoEmUmaLinha && _colunasDoIndice > 0)
        {
            _estado = EstadoDoArquivoSql.AposCreateTable;
            return;
        }

        ConferirColunasDoIndice([linha]);
        _colunasDoIndice++;
    }

    private void ConferirColunasDoIndice(string[] colunas)
    {
        if (!colunas.All(ColunaDoIndiceExiste))
        {
            throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.IndiceInvalido);
        }
    }

    private bool ColunaDoIndiceExiste(string coluna)
    {
        Match especificacao = PadroesDoArquivoSqlDne.ColunaDoIndice().Match(coluna);
        return especificacao.Success && _colunas.Contem(especificacao.Groups["nome"].Value);
    }

    private bool TentarChavePrimaria(string linha)
    {
        Match comando = PadroesDoArquivoSqlDne.ChavePrimaria().Match(linha);
        if (!comando.Success)
        {
            return false;
        }

        ConferirNome(comando);
        string[] nomes = comando.Groups["colunas"].Value.Split(',', StringSplitOptions.TrimEntries);
        return nomes.All(nome => _colunas.Contem(nome.Trim('"')))
            ? true
            : throw _linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ChavePrimariaInvalida);
    }

    private enum EstadoDoArquivoSql
    {
        AntesDaTabela,
        EmComentarioDeBloco,
        EmCreateTable,
        AguardandoPontoEVirgulaCreateTable,
        AposCreateTable,
        EmCreateIndex,
    }
}
