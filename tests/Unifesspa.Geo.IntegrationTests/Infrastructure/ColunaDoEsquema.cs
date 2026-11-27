namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>O que o catálogo do Postgres informa de uma coluna: tipo, se aceita nulo e como é gerada.</summary>
internal sealed record ColunaDoEsquema(string Tipo, string AceitaNulo, string Geracao, string Expressao);
