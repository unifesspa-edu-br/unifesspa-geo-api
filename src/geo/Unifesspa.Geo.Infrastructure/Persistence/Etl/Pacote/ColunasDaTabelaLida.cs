namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Colunas declaradas no <c>CREATE TABLE</c> de um arquivo do pacote, na ordem do arquivo,
/// com a conferência contra as colunas obrigatórias da tabela e contra os valores de cada linha.
/// </summary>
internal sealed class ColunasDaTabelaLida
{
    private readonly List<ColunaDne> _colunas = [];

    public IReadOnlyList<ColunaDne> Colunas => _colunas;

    /// <summary>Acrescenta a coluna; devolve <see langword="false"/> se o nome já existe.</summary>
    public bool Adicionar(ColunaDne coluna)
    {
        if (Contem(coluna.Nome))
        {
            return false;
        }

        _colunas.Add(coluna);
        return true;
    }

    public bool Contem(string nome) => _colunas.Exists(coluna => string.Equals(coluna.Nome, nome, StringComparison.Ordinal));

    /// <summary>Todas as colunas obrigatórias da tabela existem com o tipo esperado (colunas extras são toleradas).</summary>
    public bool AtendeObrigatorias(string tabela) =>
        TabelasDne.ColunasObrigatorias(tabela).All(obrigatoria => _colunas.Contains(obrigatoria));

    /// <summary>A quantidade e o tipo dos valores combinam com as colunas (nulo vale para qualquer tipo).</summary>
    public bool AceitaValores(object?[] valores)
    {
        if (valores.Length != _colunas.Count)
        {
            return false;
        }

        for (int i = 0; i < valores.Length; i++)
        {
            if (!TipoCombina(_colunas[i].Tipo, valores[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TipoCombina(TipoDeColunaDne tipo, object? valor) => tipo switch
    {
        TipoDeColunaDne.Texto => valor is null or string,
        _ => valor is null or int,
    };
}
