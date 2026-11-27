namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Globalization;

/// <summary>
/// Classifica as entradas do zip e acumula o que está errado. Nomes vindos do arquivo nunca
/// entram nas mensagens: só contagens, nomes das 15 tabelas e versões de seis dígitos.
/// </summary>
internal sealed class ContagemDasEntradasDne
{
    private const string ExtensaoDaTabela = ".sql";

    private readonly Dictionary<string, int> _porTabela = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _versoes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pastas = new(StringComparer.Ordinal);
    private int _nomesInvalidos;
    private int _foraDaLista;
    private int _acompanhantesGrandes;
    private int _cifradas;
    private bool _haEntradaDePasta;

    public IReadOnlyCollection<string> Versoes => _versoes;

    public void Registrar(EntradaDoPacoteDne entrada)
    {
        _cifradas += entrada.Cifrada ? 1 : 0;
        CaminhoDaEntradaDne.PartesDaEntrada? partes = CaminhoDaEntradaDne.Separar(entrada.Nome);
        if (partes is null)
        {
            _nomesInvalidos++;
            return;
        }

        _pastas.Add(partes.Pasta);
        Classificar(entrada, partes);
    }

    public void ReportarFaltas(List<string> faltas)
    {
        Reportar(faltas, _nomesInvalidos, "entrada(s) com nome inválido (mais de 128 caracteres, fora do padrão pasta/arquivo ou entrada de pasta repetida ou com conteúdo)");
        if (_pastas.Count > 1)
        {
            faltas.Add("As entradas não estão todas na mesma pasta.");
        }

        Reportar(faltas, _foraDaLista, "entrada(s) que não pertencem ao pacote");
        Reportar(faltas, _cifradas, "entrada(s) cifrada(s)");
        Reportar(faltas, _acompanhantesGrandes, "arquivo(s) que acompanham o pacote com tamanho declarado acima de 5 MiB");
        ReportarTabelas(faltas);
    }

    private static void Reportar(List<string> faltas, int quantidade, string descricao)
    {
        if (quantidade > 0)
        {
            faltas.Add(string.Create(CultureInfo.InvariantCulture, $"{quantidade} {descricao}."));
        }
    }

    private void Classificar(EntradaDoPacoteDne entrada, CaminhoDaEntradaDne.PartesDaEntrada partes)
    {
        if (partes.EhPasta)
        {
            RegistrarEntradaDePasta(entrada);
        }
        else if (partes.Nome.EndsWith(ExtensaoDaTabela, StringComparison.OrdinalIgnoreCase))
        {
            RegistrarTabela(partes.Nome);
        }
        else if (entrada.TamanhoDeclarado > LimitesDoPacoteDne.BytesPorArquivoQueAcompanhaOPacote)
        {
            _acompanhantesGrandes++;
        }
    }

    private void RegistrarEntradaDePasta(EntradaDoPacoteDne entrada)
    {
        if (_haEntradaDePasta || entrada.TamanhoDeclarado != 0)
        {
            _nomesInvalidos++;
        }

        _haEntradaDePasta = true;
    }

    private void RegistrarTabela(string nome)
    {
        if (!NomeDaTabelaDne.TentarLerEntrada(nome, out string versao, out string tabela) || !TabelasDne.Contem(tabela))
        {
            _foraDaLista++;
            return;
        }

        _versoes.Add(versao);
        _porTabela[tabela] = _porTabela.GetValueOrDefault(tabela) + 1;
    }

    private void ReportarTabelas(List<string> faltas)
    {
        string[] ausentes = [.. TabelasDne.Nomes.Where(nome => !_porTabela.ContainsKey(nome))];
        string[] repetidas = [.. TabelasDne.Nomes.Where(nome => _porTabela.GetValueOrDefault(nome) > 1)];
        if (ausentes.Length > 0)
        {
            faltas.Add($"Tabelas ausentes: {string.Join(", ", ausentes)}.");
        }

        if (repetidas.Length > 0)
        {
            faltas.Add($"Tabelas repetidas: {string.Join(", ", repetidas)}.");
        }
    }
}
