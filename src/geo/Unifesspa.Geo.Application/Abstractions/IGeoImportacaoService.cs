namespace Unifesspa.Geo.Application.Abstractions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Porta da API para a atualização da base dos Correios. O ETL é um serviço de Infrastructure
/// (ADR-0092), não um command Wolverine — não há regra de negócio nem evento de domínio, é
/// carga de reference data autoritativo. Esta porta expõe à borda apenas o
/// <strong>registro do pacote recebido</strong> e o <strong>acompanhamento</strong>; a execução
/// pesada roda em segundo plano.
/// </summary>
public interface IGeoImportacaoService
{
    /// <summary>
    /// Recebe o pacote enviado no <paramref name="corpo"/> e registra a carga. Recusa, sem ler o
    /// corpo, quando já há carga em andamento; recusa pacote acima do limite ou inválido, versão
    /// anterior à última aplicada e carga simultânea; devolve a publicação vigente quando o resumo
    /// SHA-256 é o da última carga concluída; senão registra a execução como em andamento e a
    /// enfileira. O arquivo gravado é apagado em toda recusa e na repetição.
    /// </summary>
    /// <param name="corpo">Fluxo com o zip do pacote, lido uma única vez.</param>
    /// <param name="disparadoPor">Subject do administrador.</param>
    Task<Result<EnvioDoPacoteRegistrado>> IniciarAsync(Stream corpo, string disparadoPor, CancellationToken cancellationToken);

    /// <summary>
    /// Obtém o estado e o relatório de uma execução pelo Id, ou <see langword="null"/>
    /// quando não existe.
    /// </summary>
    Task<ImportacaoGeoDto?> ObterAsync(Guid id, CancellationToken cancellationToken);
}
