namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Pacote dos Correios gravado e conferido: onde está o arquivo, o resumo SHA-256 (hexadecimal
/// minúsculo) do conteúdo original, o tamanho em bytes e a versão AAAAMM lida dos nomes das tabelas.
/// </summary>
internal sealed record PacoteDneRecebido(string Caminho, string Sha256, long TamanhoEmBytes, string Versao);
