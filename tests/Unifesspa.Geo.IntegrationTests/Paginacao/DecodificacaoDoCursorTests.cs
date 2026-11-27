namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Buffers.Text;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.IntegrationTests.Cifragem;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.Kernel.Pagination;

public sealed class DecodificacaoDoCursorTests
{
    private static readonly string[] NomesDaCarga =
        ["after", "limit", "resourceTag", "expiresAt", "direction", "userId", "sortKey"];

    private readonly RelogioFixo _relogio = new();
    private readonly LocalAesEncryptionService _cifragem;
    private readonly CursorEncoder _codificador;

    public DecodificacaoDoCursorTests()
    {
        _cifragem = new LocalAesEncryptionService(
            Options.Create(OpcoesDeCifragemParaTeste.Local(OpcoesDeCifragemParaTeste.ChaveEmBase64De(32))),
            NullLogger<LocalAesEncryptionService>.Instance);
        _codificador = new CursorEncoder(_cifragem, _relogio);
    }

    [Theory(DisplayName = "Cursor em branco é inválido")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Cursor_EmBranco_EInvalido(string cursor)
    {
        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(cursor);

        ConferirInvalido(resultado);
    }

    [Theory(DisplayName = "Texto que não é Base64 de endereço é inválido")]
    [InlineData("@@@@")]
    [InlineData("a")]
    [InlineData("ab+/")]
    public async Task Cursor_SemBase64DeEndereco_EInvalido(string cursor)
    {
        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(cursor);

        ConferirInvalido(resultado);
    }

    [Fact(DisplayName = "Base64 que não é um cursor cifrado, curto demais para conter nonce e tag, é inválido")]
    public async Task Cursor_Base64CurtoDemais_EInvalido()
    {
        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(Base64Url.EncodeToString("abc"u8));

        ConferirInvalido(resultado);
    }

    [Fact(DisplayName = "Cursor com a cifra adulterada é inválido")]
    public async Task Cursor_CifraAdulterada_EInvalido()
    {
        byte[] cifrado = Base64Url.DecodeFromChars(await _codificador.EncodeAsync(CargaValida()));
        cifrado[^1] ^= 0xFF;

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(Base64Url.EncodeToString(cifrado));

        ConferirInvalido(resultado);
    }

    [Fact(DisplayName = "Cursor cifrado com outro nome de chave é inválido")]
    public async Task Cursor_CifradoComOutraChave_EInvalido()
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(CargaValida());
        byte[] cifrado = await _cifragem.EncryptAsync("outra-chave", json);

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(Base64Url.EncodeToString(cifrado));

        ConferirInvalido(resultado);
    }

    [Theory(DisplayName = "Cursor cujo conteúdo decifrado não é a carga esperada é inválido")]
    [InlineData("isto não é json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task Cursor_ConteudoQueNaoECarga_EInvalido(string conteudo)
    {
        string cursor = await CursorComConteudoAsync(Encoding.UTF8.GetBytes(conteudo));

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(cursor);

        ConferirInvalido(resultado);
    }

    [Theory(DisplayName = "Carga sem chave de continuação ou sem recurso é inválida")]
    [InlineData("", "estados")]
    [InlineData("018f0000-0000-7000-8000-000000000001", "")]
    public async Task Cursor_SemContinuacaoOuSemRecurso_EInvalido(string after, string recurso)
    {
        string cursor = await _codificador.EncodeAsync(CargaValida() with { After = after, ResourceTag = recurso });

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(cursor);

        ConferirInvalido(resultado);
    }

    [Theory(DisplayName = "Cursor que vence no instante atual ou antes está expirado e devolve a carga")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public async Task Cursor_VencidoNoInstanteAtualOuAntes_DevolveExpiradoComACarga(int segundosAteOVencimento)
    {
        CursorPayload carga = CargaValida() with { ExpiresAt = _relogio.GetUtcNow().AddSeconds(segundosAteOVencimento) };

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(await _codificador.EncodeAsync(carga));

        resultado.Status.Should().Be(CursorDecodeStatus.Expired);
        resultado.Payload.Should().Be(carga);
    }

    [Fact(DisplayName = "Cursor que vence um segundo depois do instante atual ainda vale e devolve a carga")]
    public async Task Cursor_VenceUmSegundoDepois_DevolveSucessoComACarga()
    {
        CursorPayload carga = CargaValida() with { ExpiresAt = _relogio.GetUtcNow().AddSeconds(1) };

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(await _codificador.EncodeAsync(carga));

        resultado.Status.Should().Be(CursorDecodeStatus.Success);
        resultado.Payload.Should().Be(carga);
    }

    [Fact(DisplayName = "A carga completa, com usuário e chave de ordenação, volta igual da ida e volta")]
    public async Task Cursor_CargaCompleta_VoltaIgual()
    {
        CursorPayload carga = CargaValida() with { Direction = PaginationDirection.Prev, UserId = "usuario-de-teste", SortKey = "maraba" };

        CursorDecodeResult resultado = await _codificador.TryDecodeAsync(await _codificador.EncodeAsync(carga));

        resultado.Payload.Should().Be(carga);
    }

    [Fact(DisplayName = "A carga é escrita com os nomes after, limit, resourceTag, expiresAt, direction, userId e sortKey, mesmo quando nulos")]
    public async Task Cursor_Carga_TemOsNomesJsonDoFormato()
    {
        using JsonDocument json = await DecifrarAsync(await _codificador.EncodeAsync(CargaValida()));

        json.RootElement.EnumerateObject().Select(propriedade => propriedade.Name).Should().Equal(NomesDaCarga);
        json.RootElement.GetProperty("userId").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("sortKey").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory(DisplayName = "A direção vai na carga como número: 0 para a seguinte e 1 para a anterior")]
    [InlineData(PaginationDirection.Next, 0)]
    [InlineData(PaginationDirection.Prev, 1)]
    public async Task Cursor_Direcao_VaiComoNumero(PaginationDirection direcao, int numero)
    {
        using JsonDocument json = await DecifrarAsync(await _codificador.EncodeAsync(CargaValida() with { Direction = direcao }));

        json.RootElement.GetProperty("direction").GetInt32().Should().Be(numero);
    }

    private CursorPayload CargaValida() =>
        new("018f0000-0000-7000-8000-000000000001", 20, "estados", _relogio.GetUtcNow().AddMinutes(15), PaginationDirection.Next);

    private async Task<string> CursorComConteudoAsync(byte[] conteudo) =>
        Base64Url.EncodeToString(await _cifragem.EncryptAsync(CursorEncoder.KeyName, conteudo));

    private async Task<JsonDocument> DecifrarAsync(string cursor) =>
        JsonDocument.Parse(await _cifragem.DecryptAsync(CursorEncoder.KeyName, Base64Url.DecodeFromChars(cursor)));

    private static void ConferirInvalido(CursorDecodeResult resultado)
    {
        resultado.Status.Should().Be(CursorDecodeStatus.Invalid);
        resultado.Payload.Should().BeNull();
    }
}
