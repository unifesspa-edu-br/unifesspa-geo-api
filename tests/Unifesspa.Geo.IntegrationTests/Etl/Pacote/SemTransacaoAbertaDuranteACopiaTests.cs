namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// A fonte da execução é preparada depois de a transação da publicação ser aberta no contexto.
/// Como o Npgsql só envia o BEGIN junto do primeiro comando, o servidor não pode ter transação
/// aberta e parada durante a cópia do pacote: transação longa e ociosa seguraria recursos do
/// banco por todo o carregamento.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class SemTransacaoAbertaDuranteACopiaTests : IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly ConexaoDoBancoDaCarga _conexao;
    private readonly BancoDaCarga _banco;
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacote-dne-{Guid.NewGuid():N}");

    public SemTransacaoAbertaDuranteACopiaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _conexao = new ConexaoDoBancoDaCarga(fixture);
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Fact(DisplayName = "Durante a cópia do pacote para o esquema de carga não há transação aberta e parada no servidor")]
    public async Task DuranteACopia_NaoHaTransacaoAbertaNoServidor()
    {
        await _banco.LimparAsync();
        ArquivoDoPacoteDne arquivos = new(_diretorio);
        FonteObservadoraDaTransacao fonte = new(
            new EsquemaDeCargaDne(_conexao.Configuracao, arquivos, NullLogger<EsquemaDeCargaDne>.Instance), _conexao);
        using MontagemDaCarga montagem = new(_fixture);
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoNova);
        PacoteSinteticoDne.Completo(VersoesFicticias.VersaoNova).Gravar(_diretorio, id);

        await montagem.ExecutarAsync(id, fonte);

        (await _banco.ExecucaoAsync(id)).Status.Should().Be(StatusImportacao.Concluida);
        fonte.Amostras.Should().BeGreaterThan(0);
        fonte.MaximoDeSessoesComTransacaoAberta.Should().Be(0, "o BEGIN só vai ao servidor junto do primeiro comando da publicação");
    }
}

/// <summary>
/// Prepara a fonte pela implementação real e, enquanto isso, consulta o servidor a intervalos curtos
/// para ver se alguma sessão está com transação aberta e parada.
/// </summary>
internal sealed class FonteObservadoraDaTransacao : IFonteDaExecucaoDne
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(20);

    private readonly IFonteDaExecucaoDne _interna;
    private readonly ConexaoDoBancoDaCarga _conexao;

    public FonteObservadoraDaTransacao(IFonteDaExecucaoDne interna, ConexaoDoBancoDaCarga conexao)
    {
        _interna = interna;
        _conexao = conexao;
    }

    public int Amostras { get; private set; }

    public long MaximoDeSessoesComTransacaoAberta { get; private set; }

    public async Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken)
    {
        using CancellationTokenSource fim = new();
        await AmostrarAsync();
        Task amostragem = AmostrarAteOFimAsync(fim.Token);
        try
        {
            return await _interna.PrepararAsync(execucaoId, versao, cancellationToken);
        }
        finally
        {
            await fim.CancelAsync();
            await amostragem;
            await AmostrarAsync();
        }
    }

    public Task DescartarPacoteAsync(Guid execucaoId) => _interna.DescartarPacoteAsync(execucaoId);

    public Task<int> DescartarSobrasAsync(CancellationToken cancellationToken) => _interna.DescartarSobrasAsync(cancellationToken);

    private async Task AmostrarAteOFimAsync(CancellationToken fim)
    {
        while (!fim.IsCancellationRequested)
        {
            await AmostrarAsync();
            await Task.Delay(Intervalo, TimeProvider.System, CancellationToken.None);
        }
    }

    private async Task AmostrarAsync()
    {
        long sessoes = await _conexao.SessoesComTransacaoAbertaAsync();
        Amostras++;
        MaximoDeSessoesComTransacaoAberta = Math.Max(MaximoDeSessoesComTransacaoAberta, sessoes);
    }
}
