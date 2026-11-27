namespace Unifesspa.Geo.IntegrationTests.Admin;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Porta das importações com respostas definidas pelo teste; guarda o que recebeu.</summary>
internal sealed class ServicoDeImportacaoFalso : IGeoImportacaoService
{
    public Result<EnvioDoPacoteRegistrado> ResultadoDoEnvio { get; set; } =
        Result<EnvioDoPacoteRegistrado>.Success(new(Guid.NewGuid(), DesfechoDoEnvioDoPacote.CargaEnfileirada));

    public ImportacaoGeoDto? Execucao { get; set; }

    public int ChamadasDeIniciar { get; private set; }

    public Stream? CorpoRecebido { get; private set; }

    public string? DisparadoPor { get; private set; }

    public Task<Result<EnvioDoPacoteRegistrado>> IniciarAsync(
        Stream corpo,
        string disparadoPor,
        CancellationToken cancellationToken)
    {
        ChamadasDeIniciar++;
        CorpoRecebido = corpo;
        DisparadoPor = disparadoPor;
        return Task.FromResult(ResultadoDoEnvio);
    }

    public Task<ImportacaoGeoDto?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Execucao);
}
