namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Quando o servidor recusa o corpo por tamanho durante a leitura (envio sem tamanho declarado), o
/// cliente recebe a resposta 413 em formato de problema, não um erro interno.
/// </summary>
public sealed class EnvioDoPacoteRecusadoPeloServidorNaLeituraTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _fabrica;

    public EnvioDoPacoteRecusadoPeloServidorNaLeituraTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact(DisplayName = "Recusa do servidor por tamanho durante a leitura devolve 413 com o problema do pacote acima do limite")]
    public async Task RecusaDoServidorNaLeitura_DevolveProblema413()
    {
        await using HostDoEnvioDoPacote host = new(
            _fabrica,
            ajustarServicos: servicos => servicos.AddSingleton<IGeoImportacaoService>(new ServicoQueOServidorRecusa()));
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.NaoEhZip());

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("uniplus.geo.importacao.pacote_acima_do_limite");
    }

    private sealed class ServicoQueOServidorRecusa : IGeoImportacaoService
    {
        public Task<Result<EnvioDoPacoteRegistrado>> IniciarAsync(Stream corpo, string disparadoPor, CancellationToken cancellationToken) =>
            throw new BadHttpRequestException("Corpo acima do limite do servidor.", StatusCodes.Status413PayloadTooLarge);

        public Task<ImportacaoGeoDto?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ImportacaoGeoDto?>(null);
    }
}
