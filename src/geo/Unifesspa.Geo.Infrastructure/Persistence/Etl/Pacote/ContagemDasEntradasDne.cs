namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Globalization;

/// <summary>
/// Classifica as entradas do zip e acumula o que está errado. Nomes vindos do arquivo nunca
/// entram nas mensagens: só contagens, nomes das 15 tabelas e versões de seis dígitos.
/// </summary>
internal sealed class ContagemDasEntradasDne
{
    public const string ImagemAuxiliar = "0relacionamentos.jpg";

    private readonly Dictionary<string, int> _porTabela = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _versoes = new(StringComparer.Ordinal);
    private int _nomesInvalidos;
    private int _foraDaLista;
    private int _imagens;
    private int _cifradas;

    public IReadOnlyCollection<string> Versoes => _versoes;

    public void Registrar(EntradaDoPacoteDne entrada)
    {
        _cifradas += entrada.Cifrada ? 1 : 0;
        if (!NomeEhSeguro(entrada.Nome))
        {
            _nomesInvalidos++;
        }
        else if (string.Equals(entrada.Nome, ImagemAuxiliar, StringComparison.Ordinal))
        {
            _imagens++;
        }
        else
        {
            RegistrarTabela(entrada.Nome);
        }
    }

    public void ReportarFaltas(List<string> faltas)
    {
        Reportar(faltas, _nomesInvalidos, "entrada(s) com nome inválido (mais de 128 caracteres ou com '/', '\\' ou '..')");
        Reportar(faltas, _foraDaLista, "entrada(s) que não pertencem ao pacote");
        Reportar(faltas, _cifradas, "entrada(s) cifrada(s)");
        if (_imagens > 1)
        {
            faltas.Add($"A imagem {ImagemAuxiliar} aparece mais de uma vez.");
        }

        ReportarTabelas(faltas);
    }

    private static bool NomeEhSeguro(string nome) =>
        nome.Length is > 0 and <= LimitesDoPacoteDne.CaracteresNoNomeDaEntrada
        && !nome.Contains('/', StringComparison.Ordinal)
        && !nome.Contains('\\', StringComparison.Ordinal)
        && !nome.Contains("..", StringComparison.Ordinal)
        && !nome.Any(char.IsControl);

    private static void Reportar(List<string> faltas, int quantidade, string descricao)
    {
        if (quantidade > 0)
        {
            faltas.Add(string.Create(CultureInfo.InvariantCulture, $"{quantidade} {descricao}."));
        }
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
