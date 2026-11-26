namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

/// <summary>
/// Os comandos SQL que levam uma tabela de destino do arquivo de origem até o banco: criação
/// da tabela temporária, cópia binária para ela, mesclagem no destino e remoção da temporária.
/// </summary>
internal sealed record ComandosDaTabelaPorCopia(
    string TabelaDeDestino,
    string CriacaoDaTabelaTemporaria,
    string Copia,
    string Mesclagem,
    string DescarteDaTabelaTemporaria);
