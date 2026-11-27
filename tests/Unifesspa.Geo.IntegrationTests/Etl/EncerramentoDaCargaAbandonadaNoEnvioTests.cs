namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// O envio do pacote encerra como falha a execução que ficou em andamento quando nenhuma instância
/// a executa (a trava da carga está livre) e segue; com a trava ocupada, a execução é de uma carga
/// viva e o envio é recusado sem ler o corpo.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class EncerramentoDaCargaAbandonadaNoEnvioTests : IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly EnvioDoPacoteParaTeste _envio = new();
    private readonly RelogioFixo _relogio = new();

    public EncerramentoDaCargaAbandonadaNoEnvioTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Com a trava livre, o envio encerra a execução abandonada como falha e registra a nova")]
    public async Task TravaLivre_EncerraAAbandonada_ERegistraANova()
    {
        await _banco.LimparAsync();
        Guid abandonada = await SemearEmAndamentoAsync("202610");
        DateTimeOffset instante = _relogio.GetUtcNow();

        Result<EnvioDoPacoteRegistrado> resultado = await EnviarAsync(_envio.Receber("202611").Abrir());

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value!.Desfecho.Should().Be(DesfechoDoEnvioDoPacote.CargaEnfileirada);
        GeoImportacaoExecucao antiga = await _banco.ExecucaoAsync(abandonada);
        antiga.Status.Should().Be(StatusImportacao.Falhou);
        antiga.Mensagem.Should().Be(EncerramentoDasCargasAbandonadas.MensagemDaCargaAbandonada);
        antiga.ConcluidoEm.Should().Be(instante);
        (await _banco.ExecucaoAsync(resultado.Value.ExecucaoId)).Status.Should().Be(StatusImportacao.EmAndamento);
    }

    [Fact(DisplayName = "Com a trava ocupada por outra instância, o envio é recusado sem ler o corpo e a execução segue em andamento")]
    public async Task TravaOcupada_RecusaSemLerOCorpo_EMantemAExecucao()
    {
        await _banco.LimparAsync();
        Guid viva = await SemearEmAndamentoAsync("202610");
        using FluxoComGancho corpo = new(_envio.Receber("202611").Bytes, () => throw new InvalidOperationException("o corpo foi lido"));

        Result<EnvioDoPacoteRegistrado> resultado;
        await using (await TravaDaCargaParaTeste.AdquirirAsync(_fixture.ConnectionString))
        {
            resultado = await EnviarAsync(corpo);
        }

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.ImportacaoEmAndamento);
        (await _banco.ExecucaoAsync(viva)).Status.Should().Be(StatusImportacao.EmAndamento);
        _envio.NomesDosArquivos().Should().BeEmpty();
    }

    [Fact(DisplayName = "O encerramento feito pelo envio descarta o esquema de carga que sobrou")]
    public async Task Encerramento_DescartaOEsquemaQueSobrou()
    {
        await _banco.LimparAsync();
        ConexaoDoBancoDaCarga conexao = new(_fixture);
        _envio.Fonte = new EsquemaDeCargaDne(conexao.Configuracao, _envio.Arquivos, NullLogger<EsquemaDeCargaDne>.Instance);
        Guid abandonada = await SemearEmAndamentoAsync("202610");
        string sobra = ComandosDoEsquemaDeCarga.Nome(abandonada);
        await conexao.ExecutarAsync($"CREATE SCHEMA \"{sobra}\"");

        Result<EnvioDoPacoteRegistrado> resultado = await EnviarAsync(_envio.Receber("202611").Abrir());

        resultado.IsSuccess.Should().BeTrue();
        (await conexao.ExisteEsquemaAsync(sobra)).Should().BeFalse();
    }

    [Fact(DisplayName = "Falha ao descartar os esquemas mantém a execução em andamento e o envio seguinte a encerra")]
    public async Task FalhaNoDescarte_MantemEmAndamento_EOEnvioSeguinteEncerra()
    {
        await _banco.LimparAsync();
        _envio.Fonte = new FonteQueFalhaAoDescartarSobras(new InvalidOperationException("Falha simulada no descarte."));
        Guid abandonada = await SemearEmAndamentoAsync("202610");

        Func<Task> envio = () => EnviarAsync(_envio.Receber("202611").Abrir());

        await envio.Should().ThrowAsync<InvalidOperationException>();
        (await _banco.ExecucaoAsync(abandonada)).Status.Should().Be(StatusImportacao.EmAndamento);
        _envio.Fonte = new FabricaDeFonteFixa();
        (await EnviarAsync(_envio.Receber("202611").Abrir())).IsSuccess.Should().BeTrue();
        (await _banco.ExecucaoAsync(abandonada)).Status.Should().Be(StatusImportacao.Falhou);
    }

    [Fact(DisplayName = "Cancelamento durante o descarte dos esquemas mantém a execução em andamento e solta a trava")]
    public async Task CancelamentoNoDescarte_MantemAExecucaoEmAndamento()
    {
        await _banco.LimparAsync();
        _envio.Fonte = new FonteQueFalhaAoDescartarSobras(new OperationCanceledException());
        Guid abandonada = await SemearEmAndamentoAsync("202610");

        Func<Task> envio = () => EnviarAsync(_envio.Receber("202611").Abrir());

        await envio.Should().ThrowAsync<OperationCanceledException>();
        (await _banco.ExecucaoAsync(abandonada)).Status.Should().Be(StatusImportacao.EmAndamento);
        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull("o encerramento interrompido solta a trava");
        await trava!.DisposeAsync();
    }

    private async Task<Guid> SemearEmAndamentoAsync(string versao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao
            .Iniciar(_envio.Recebido(versao), "teste", _relogio.GetUtcNow() - TimeSpan.FromMinutes(5))
            .Value!;
        contexto.ImportacaoExecucoes.Add(execucao);
        await contexto.SaveChangesAsync();
        return execucao.Id;
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> EnviarAsync(Stream corpo)
    {
        GeoImportacaoFila fila = new();
        await using GeoDbContext contexto = _fixture.CreateDbContext(_relogio);
        RegistroDasImportacoes registro = _envio.CriarRegistro(
            contexto,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            _relogio,
            NullLogger<RegistroDasImportacoes>.Instance);

        try
        {
            return await registro.IniciarAsync(corpo, "teste", CancellationToken.None);
        }
        finally
        {
            foreach (CargaReservada enfileirada in fila.DrenarRestante())
            {
                await enfileirada.DisposeAsync();
            }
        }
    }
}
