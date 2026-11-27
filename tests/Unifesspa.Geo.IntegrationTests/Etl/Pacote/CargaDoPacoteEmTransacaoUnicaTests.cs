namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Carga a partir de um pacote sintético gravado no diretório de pacotes: o esquema de carga é
/// populado a partir do zip, a publicação segue na transação única e, ao fim ou em falha, o
/// esquema e o arquivo somem. Valores fictícios; o marcador sigiloso nunca chega aos logs nem à mensagem.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CargaDoPacoteEmTransacaoUnicaTests : IDisposable
{
    private const string Marcador = "SEGREDO-FICTICIO-7391";

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly ConexaoDoBancoDaCarga _conexao;
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacote-dne-{Guid.NewGuid():N}");
    private readonly ArquivoDoPacoteDne _arquivos;

    public CargaDoPacoteEmTransacaoUnicaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
        _conexao = new ConexaoDoBancoDaCarga(fixture);
        _arquivos = new ArquivoDoPacoteDne(_diretorio);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Fact(DisplayName = "Pacote completo popula o esquema, publica na transação única, conclui a execução e descarta esquema e arquivo")]
    public async Task PacoteCompleto_PublicaEDescarta()
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoNova);
        PacoteSinteticoDne.Completo(VersoesFicticias.VersaoNova).Gravar(_diretorio, id);

        await montagem.ExecutarAsync(id, EsquemaReal());

        (await _banco.ExecucaoAsync(id)).Status.Should().Be(StatusImportacao.Concluida);
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Cidades.CountAsync(c => c.VersaoDataset == VersoesFicticias.VersaoNova && c.Vigente)).Should().Be(1);
        (await leitura.Logradouros.CountAsync()).Should().Be(2);
        await ConferirSemEsquemaNemArquivoAsync(id);
    }

    [Fact(DisplayName = "Linha malformada no meio do pacote mantém a base anterior, falha com tabela e linha, descarta esquema e arquivo e não vaza o marcador")]
    public async Task LinhaMalformada_MantemBaseAnteriorEDescarta()
    {
        await PrepararBaseAnteriorAsync();

        using MontagemDaCarga montagem = new(_fixture);
        PacoteSinteticoDne pacote = PacoteSinteticoDne.Completo(VersoesFicticias.VersaoNova)
            .ComLinhaBruta("logradouro", $"'{Marcador}', E'x'");
        Guid id = await RegistrarExecucaoComPacoteAsync(montagem, pacote);

        await montagem.ExecutarAsync(id, EsquemaReal());

        GeoImportacaoExecucao execucao = await _banco.ExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.Mensagem.Should().Contain("Tabela logradouro, linha").And.NotContain(Marcador);
        montagem.Logs.TextoCompleto.Should().NotContain(Marcador).And.Contain("TabelaDoPacote=logradouro");
        await ConferirBaseAnteriorSemEsquemaNemArquivoAsync(id);
    }

    [Fact(DisplayName = "Falha na publicação depois de o esquema estar pronto mantém a base anterior e descarta esquema e arquivo")]
    public async Task FalhaNaPublicacao_MantemBaseAnteriorEDescarta()
    {
        await PrepararBaseAnteriorAsync();

        using MontagemDaCarga montagem = new(_fixture)
        {
            DecorarFolhas = folhas => new FolhasComGancho(
                folhas, () => throw new InvalidOperationException($"falha com {Marcador}")),
        };
        Guid id = await RegistrarExecucaoComPacoteAsync(montagem, PacoteSinteticoDne.Completo(VersoesFicticias.VersaoNova));

        await montagem.ExecutarAsync(id, EsquemaReal());

        (await _banco.ExecucaoAsync(id)).Status.Should().Be(StatusImportacao.Falhou);
        montagem.Logs.TextoCompleto.Should().NotContain(Marcador);
        await ConferirBaseAnteriorSemEsquemaNemArquivoAsync(id);
    }

    private EsquemaDeCargaDne EsquemaReal() =>
        new(_conexao.Configuracao, _arquivos, NullLogger<EsquemaDeCargaDne>.Instance);

    private async Task<Guid> RegistrarExecucaoComPacoteAsync(MontagemDaCarga montagem, PacoteSinteticoDne pacote)
    {
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoNova);
        pacote.Gravar(_diretorio, id);
        return id;
    }

    private async Task PrepararBaseAnteriorAsync()
    {
        await _banco.LimparAsync();
        using MontagemDaCarga anterior = new(_fixture);
        await anterior.PublicarAsync(VersoesFicticias.Anterior());
    }

    private async Task ConferirBaseAnteriorSemEsquemaNemArquivoAsync(Guid id)
    {
        await _banco.ConferirSomenteVersaoAnteriorAsync();
        await ConferirSemEsquemaNemArquivoAsync(id);
    }

    private async Task ConferirSemEsquemaNemArquivoAsync(Guid id)
    {
        (await _conexao.ExisteEsquemaAsync(ComandosDoEsquemaDeCarga.Nome(id))).Should().BeFalse("o esquema é descartado ao fim");
        File.Exists(_arquivos.CaminhoDe(id)).Should().BeFalse("o arquivo é apagado ao fim");
    }
}
