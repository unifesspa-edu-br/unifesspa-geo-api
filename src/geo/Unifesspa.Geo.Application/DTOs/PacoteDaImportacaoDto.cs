namespace Unifesspa.Geo.Application.DTOs;

/// <summary>
/// Pacote dos Correios que originou a execução: resumo SHA-256 (hexadecimal minúsculo) e tamanho
/// em bytes. O nome do arquivo enviado não é guardado nem exposto.
/// </summary>
public sealed record PacoteDaImportacaoDto(string Sha256, long TamanhoEmBytes);
