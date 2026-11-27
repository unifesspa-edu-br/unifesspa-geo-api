namespace Unifesspa.Geo.IntegrationTests.Inicializacao;

using AwesomeAssertions;

using Microsoft.Extensions.Hosting;

/// <summary>
/// A API parte na ordem: migrações do banco, gatilhos da carga e mensageria. O host inicia os
/// serviços hospedados um de cada vez, na ordem em que o <c>Program</c> os registrou.
/// </summary>
public sealed class OrdemDaInicializacaoDaApiTests : IClassFixture<ApiComInicializacaoObservada>
{
    private readonly ApiComInicializacaoObservada _api;

    public OrdemDaInicializacaoDaApiTests(ApiComInicializacaoObservada api)
    {
        _api = api;
    }

    [Fact(DisplayName = "A partida da API passa pelas migrações, pelos gatilhos da carga e pela mensageria")]
    public void Partida_PassaPelosTresPassos()
    {
        foreach (PassoDaInicializacao passo in Enum.GetValues<PassoDaInicializacao>())
        {
            _api.Ordem.Observou(passo).Should().BeTrue($"o passo {passo} precisa fazer parte da partida da API");
        }
    }

    [Fact(DisplayName = "As migrações do banco terminam antes de a mensageria começar")]
    public void Migracoes_TerminamAntesDaMensageria() =>
        AfirmarQueTerminaAntes(
            PassoDaInicializacao.MigracoesDoBanco,
            PassoDaInicializacao.Mensageria,
            "as migrações do banco vêm antes da mensageria; o Wolverine cria as tabelas e lê o banco ao iniciar");

    [Fact(DisplayName = "As migrações do banco terminam antes de os gatilhos da carga começarem")]
    public void Migracoes_TerminamAntesDosGatilhos() =>
        AfirmarQueTerminaAntes(
            PassoDaInicializacao.MigracoesDoBanco,
            PassoDaInicializacao.GatilhosDaCarga,
            "os gatilhos da carga leem a tabela de execuções, que as migrações do banco criam");

    [Fact(DisplayName = "Os gatilhos da carga terminam antes de a mensageria começar")]
    public void Gatilhos_TerminamAntesDaMensageria() =>
        AfirmarQueTerminaAntes(
            PassoDaInicializacao.GatilhosDaCarga,
            PassoDaInicializacao.Mensageria,
            "os gatilhos da carga ficam antes da mensageria, como o Program registra");

    [Fact(DisplayName = "Nenhum serviço hospedado da API tem etapa anterior ao início, que escaparia da ordem de registro")]
    public void ServicosHospedados_NaoTemEtapaAnteriorAoInicio() =>
        _api.ServicosOriginais.OfType<IHostedLifecycleService>().Should().BeEmpty(
            "o teste só observa StartAsync; um serviço com ganchos de ciclo de vida exige observar também esses ganchos");

    private void AfirmarQueTerminaAntes(PassoDaInicializacao anterior, PassoDaInicializacao seguinte, string motivo)
    {
        int terminoDoAnterior = _api.Ordem.UltimoTermino(anterior);
        int comecoDoSeguinte = _api.Ordem.PrimeiroComeco(seguinte);

        terminoDoAnterior.Should().BeGreaterThanOrEqualTo(0, $"o passo {anterior} precisa terminar na partida");
        comecoDoSeguinte.Should().BeGreaterThan(terminoDoAnterior, motivo);
    }
}
