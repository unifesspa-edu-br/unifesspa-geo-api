namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Coluna de uma tabela do pacote dos Correios: nome e tipo.</summary>
internal sealed record ColunaDne(string Nome, TipoDeColunaDne Tipo);
