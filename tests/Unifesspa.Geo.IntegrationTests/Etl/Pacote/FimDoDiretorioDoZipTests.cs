namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Buffers.Binary;
using System.IO.Compression;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// O fim do diretório do zip é conferido antes de abrir o arquivo: mais entradas que o limite configurado, zip64,
/// registro ausente ou diretório inconsistente são recusados sem carregar as entradas na memória.
/// </summary>
public sealed class FimDoDiretorioDoZipTests : IDisposable
{
    private const int TamanhoDoRegistroFinal = 22;
    private const int Limite = EtlOpcoes.QuantidadePadraoDeEntradasDoPacote;

    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"fim-zip-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Theory(DisplayName = "Zip com até 32 entradas (o padrão) passa pela conferência do fim do diretório")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(32)]
    public void ZipComAteOLimitePadrao_Passa(int entradas)
    {
        Conferir(ZipComEntradasVazias(entradas)).Should().BeNull();
    }

    [Theory(DisplayName = "Zip com mais de 32 entradas (o padrão) é recusado")]
    [InlineData(33)]
    [InlineData(300)]
    [InlineData(65_000)]
    public void ZipComMaisEntradasQueOLimitePadrao_EhRecusado(int entradas)
    {
        DomainError erro = Conferir(ZipComEntradasVazias(entradas))!;

        erro.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        erro.Message.Should().Contain("mais de 32 entradas");
    }

    [Theory(DisplayName = "O limite configurado é o que vale: no limite passa, uma acima é recusada com o valor na mensagem")]
    [InlineData(16)]
    [InlineData(100)]
    [InlineData(256)]
    public void LimiteConfigurado_EhOQueVale(int limite)
    {
        Conferir(ZipComEntradasVazias(limite), limite).Should().BeNull();
        Conferir(ZipComEntradasVazias(limite + 1), limite)!.Message.Should().Contain($"mais de {limite} entradas");
    }

    [Fact(DisplayName = "Zip com 70 mil entradas (formato zip64) é recusado")]
    public void ZipComMuitasEntradas_UsaZip64_EhRecusado()
    {
        DomainError erro = Conferir(ZipComEntradasVazias(70_000))!;

        erro.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        erro.Message.Should().Contain("zip64");
    }

    [Fact(DisplayName = "Zip sem o registro de fim do diretório (truncado) é recusado")]
    public void ZipTruncado_EhRecusado()
    {
        byte[] zip = ZipComEntradasVazias(3);

        DomainError erro = Conferir(zip[..^10])!;

        erro.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        erro.Message.Should().Contain("não é um zip válido");
    }

    [Theory(DisplayName = "Conteúdo muito curto ou sem assinatura é recusado")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(5000)]
    public void ConteudoSemAssinatura_EhRecusado(int bytes)
    {
        Conferir(new byte[bytes])!.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
    }

    [Fact(DisplayName = "Total declarado acima do limite, com diretório coerente, é recusado só pela contagem")]
    public void TotalDeclaradoAcimaDoLimite_ComDiretorioCoerente_EhRecusadoPelaContagem()
    {
        byte[] zip = ZipComEntradasVazias(3);
        AlterarRegistro(zip, total: Limite + 1);

        Conferir(zip)!.Message.Should().Contain("mais de 32 entradas");
    }

    [Fact(DisplayName = "Deslocamento e tamanho que não chegam ao registro de fim são recusados como inconsistentes")]
    public void DeslocamentoETamanhoIncoerentes_SaoRecusados()
    {
        byte[] zip = ZipComEntradasVazias(3);
        AlterarRegistro(zip, tamanho: LerTamanhoDoDiretorio(zip) - 1);

        Conferir(zip)!.Message.Should().Contain("inconsistente");
    }

    [Fact(DisplayName = "Diretório central acima de 64 KiB, coerente e com total declarado pequeno, é recusado só pelo limite de tamanho")]
    public void DiretorioCentralAcimaDe64KiB_EhRecusadoPeloLimiteDeTamanho()
    {
        byte[] zip = ZipComNomesLongos(600);
        LerTamanhoDoDiretorio(zip).Should().BeGreaterThan(LimitesDoPacoteDne.BytesNoDiretorioCentralDoZip);
        AlterarRegistro(zip, total: 5);

        Conferir(zip)!.Message.Should().Contain("64 KiB");
    }

    [Fact(DisplayName = "Assinatura no comentário com comprimento que não bate com o resto do arquivo é recusada")]
    public void AssinaturaNoComentarioComComprimentoIncompativel_EhRecusada()
    {
        byte[] zip = ZipComComentario(RegistroDeFimFalso(comprimentoDeclarado: 7, bytesRestantes: 0));

        Conferir(zip)!.Message.Should().Contain("não é um zip válido");
    }

    [Fact(DisplayName = "Assinatura no comentário com comprimento coerente é o registro avaliado, como no leitor de zip")]
    public void AssinaturaNoComentarioComComprimentoCoerente_EhORegistroAvaliado()
    {
        byte[] zip = ZipComComentario(RegistroDeFimFalso(comprimentoDeclarado: 3, bytesRestantes: 3));

        Conferir(zip)!.Message.Should().Contain("inconsistente");
    }

    [Fact(DisplayName = "Pacote com 33 entradas é recusado no recebimento com pacote_invalido e o arquivo é apagado")]
    public async Task PacoteComMaisEntradasQueOLimite_EhRecusadoNoRecebimento()
    {
        using MemoryStream corpo = new(ZipComEntradasVazias(Limite + 1));
        Guid execucao = Guid.NewGuid();

        Result<PacoteDneRecebido> resultado = await new ArquivoDoPacoteDne(_diretorio, tamanhoMaximoEmBytes: 1_000_000)
            .ReceberAsync(corpo, execucao, CancellationToken.None);

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        resultado.Error.Message.Should().NotContain("e0");
        File.Exists(Path.Combine(_diretorio, execucao.ToString("N"))).Should().BeFalse();
    }

    private static uint LerTamanhoDoDiretorio(byte[] zip) =>
        BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(zip.Length - TamanhoDoRegistroFinal + 12));

    private static void AlterarRegistro(byte[] zip, ushort? total = null, uint? tamanho = null)
    {
        int registro = zip.Length - TamanhoDoRegistroFinal;
        if (total is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(registro + 8), total.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(registro + 10), total.Value);
        }

        if (tamanho is not null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(registro + 12), tamanho.Value);
        }
    }

    // Registro de fim falso: assinatura, total 1, diretório vazio no deslocamento 0 e o comprimento de comentário declarado, com ou sem os bytes que ele anuncia.
    private static byte[] RegistroDeFimFalso(ushort comprimentoDeclarado, int bytesRestantes)
    {
        byte[] falso = new byte[TamanhoDoRegistroFinal];
        falso[0] = 0x50;
        falso[1] = 0x4B;
        falso[2] = 0x05;
        falso[3] = 0x06;
        BinaryPrimitives.WriteUInt16LittleEndian(falso.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(falso.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(falso.AsSpan(20), comprimentoDeclarado);
        return [.. falso, .. new byte[bytesRestantes]];
    }

    private static byte[] ZipComComentario(byte[] comentario)
    {
        byte[] zip = ZipComEntradasVazias(3);
        byte[] comComentario = [.. zip, .. comentario];
        BinaryPrimitives.WriteUInt16LittleEndian(comComentario.AsSpan(zip.Length - 2), (ushort)comentario.Length);
        return comComentario;
    }

    private static byte[] ZipComNomesLongos(int quantidade)
    {
        using MemoryStream saida = new();
        using (ZipArchive zip = new(saida, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < quantidade; i++)
            {
                zip.CreateEntry($"{i:D6}{new string('x', 120)}");
            }
        }

        return saida.ToArray();
    }

    private static DomainError? Conferir(byte[] bytes, int limite = Limite)
    {
        using MemoryStream fluxo = new(bytes);
        return FimDoDiretorioDoZip.Conferir(fluxo, limite);
    }

    private static byte[] ZipComEntradasVazias(int quantidade)
    {
        using MemoryStream saida = new();
        using (ZipArchive zip = new(saida, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < quantidade; i++)
            {
                zip.CreateEntry($"e{i}");
            }
        }

        return saida.ToArray();
    }
}
