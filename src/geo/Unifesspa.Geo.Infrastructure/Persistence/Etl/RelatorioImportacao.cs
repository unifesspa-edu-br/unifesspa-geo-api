namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Acumula o resultado de uma carga do ETL DNE por tabela (ADR-0092): contadores
/// (lidos/inseridos/atualizados/órfãos/duplicados/ignorados/degradados) e amostras
/// de divergência (valor cru não-parseável que degradou para <see langword="null"/>,
/// órfão, duplicata) para auditoria. <strong>Reference data público — NUNCA carrega
/// PII</strong>; é emitido ao fim via <c>[LoggerMessage]</c>. A carga não aborta por
/// dado sujo (vai para o relatório); aborta só por falha de infraestrutura.
/// </summary>
internal sealed class RelatorioImportacao
{
    private const int MaxAmostrasPorTabela = 25;

    private readonly Dictionary<string, ContadorTabela> _tabelas = new(StringComparer.Ordinal);

    public RelatorioImportacao(string versaoDataset)
    {
        VersaoDataset = versaoDataset;
    }

    /// <summary>Release DNE (AAAAMM) desta carga.</summary>
    public string VersaoDataset { get; }

    public IReadOnlyDictionary<string, ContadorTabela> Tabelas => _tabelas;

    public int TotalInseridos => _tabelas.Values.Sum(t => t.Inseridos);

    public int TotalAtualizados => _tabelas.Values.Sum(t => t.Atualizados);

    public int TotalOrfaos => _tabelas.Values.Sum(t => t.Orfaos);

    public int TotalParsesDegradados => _tabelas.Values.Sum(t => t.ParsesDegradados);

    /// <summary>Obtém (criando se necessário) o contador da tabela informada.</summary>
    public ContadorTabela Tabela(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);

        if (!_tabelas.TryGetValue(nome, out ContadorTabela? contador))
        {
            contador = new ContadorTabela(nome, MaxAmostrasPorTabela);
            _tabelas[nome] = contador;
        }

        return contador;
    }
}

/// <summary>Contadores e amostras de divergência de uma tabela na carga do ETL.</summary>
internal sealed class ContadorTabela
{
    private readonly int _maxAmostras;
    private readonly List<string> _amostras = [];

    internal ContadorTabela(string tabela, int maxAmostras)
    {
        Tabela = tabela;
        _maxAmostras = maxAmostras;
    }

    public string Tabela { get; }

    public int Lidos { get; private set; }

    public int Inseridos { get; private set; }

    public int Atualizados { get; private set; }

    /// <summary>Registros sem chave natural utilizável (descartados).</summary>
    public int IgnoradosSemChave { get; private set; }

    /// <summary>Registros cuja FK obrigatória não resolveu (descartados).</summary>
    public int Orfaos { get; private set; }

    /// <summary>Chaves naturais repetidas dentro da própria fonte (last-wins).</summary>
    public int Duplicados { get; private set; }

    /// <summary>Valores externos que não parsearam e degradaram para <see langword="null"/>.</summary>
    public int ParsesDegradados { get; private set; }

    /// <summary>Amostras (sem PII) de divergências, limitadas para não inflar o log.</summary>
    public IReadOnlyList<string> Amostras => _amostras;

    public void ContarLido() => Lidos++;

    public void ContarInserido() => Inseridos++;

    public void ContarAtualizado() => Atualizados++;

    /// <summary>
    /// Reparte o resultado da mesclagem da carga em lote: o crescimento da tabela de destino
    /// dá os inseridos, o que a mesclagem afetou além disso dá os atualizados e o que foi
    /// copiado e não afetado dá as duplicadas da própria fonte. Só a carga grava na tabela
    /// entre as duas contagens; é métrica de relatório, não controle de dados.
    /// </summary>
    public void ContarMesclagem(long copiadas, long afetadas, long crescimentoDaTabela)
    {
        Inseridos += (int)Math.Max(0, crescimentoDaTabela);
        Atualizados += (int)Math.Max(0, afetadas - crescimentoDaTabela);
        Duplicados += (int)Math.Max(0, copiadas - afetadas);
    }

    /// <summary>
    /// Devolve o valor do resultado de uma conversão recusada pelo domínio como <see langword="null"/>,
    /// contando a linha como ignorada.
    /// </summary>
    public T? AceitarOuContarIgnorado<T>(Result<T> resultado)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resultado);

        if (resultado.IsFailure)
        {
            ContarIgnoradoSemChave();
            return null;
        }

        return resultado.Value;
    }

    public void ContarIgnoradoSemChave() => IgnoradosSemChave++;

    public void ContarOrfao(string amostra)
    {
        Orfaos++;
        Amostrar(amostra);
    }

    public void ContarDuplicado(string amostra)
    {
        Duplicados++;
        Amostrar(amostra);
    }

    public void ContarParseDegradado(string amostra)
    {
        ParsesDegradados++;
        Amostrar(amostra);
    }

    private void Amostrar(string amostra)
    {
        if (_amostras.Count < _maxAmostras)
        {
            _amostras.Add(amostra);
        }
    }
}
