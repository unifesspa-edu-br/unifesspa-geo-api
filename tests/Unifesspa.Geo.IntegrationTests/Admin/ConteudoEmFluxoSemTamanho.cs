namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;
using System.Net.Http.Headers;

/// <summary>Corpo enviado em pedaços, sem <c>Content-Length</c> (transferência em blocos).</summary>
internal sealed class ConteudoEmFluxoSemTamanho : HttpContent
{
    private readonly byte[] _bytes;

    public ConteudoEmFluxoSemTamanho(byte[] bytes, string tipoDeConteudo)
    {
        _bytes = bytes;
        Headers.ContentType = new MediaTypeHeaderValue(tipoDeConteudo);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(_bytes).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
