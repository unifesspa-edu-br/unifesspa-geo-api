namespace Unifesspa.Geo.IntegrationTests.Cep;

using AwesomeAssertions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Caching;
using Unifesspa.Geo.Infrastructure.Cep;

/// <summary>
/// Falhas do cache na consulta de CEP, com substitutos do cache e do banco (sem Redis nem Docker):
/// falha ao ler a entrada, ao ler o selo de versão ou ao gravar a entrada não impede a resposta,
/// que vem do banco; o cancelamento, ao contrário, não é engolido.
/// </summary>
public sealed class FalhasDoCacheDoCepTests
{
    private const string Selo = "202601";
    private const string Cep = "01001000";
    private const string ChaveCep = $"geo:cep:f2:v{Selo}:{Cep}";

    private static CepResolvidoDto Endereco() => new(
        Cep, "Praça", "Praça da Sé", null, "Sé", null, "São Paulo", "3550308", "SP",
        -23.55m, -46.63m, CepResolucao.NivelLogradouro, CepResolucao.OrigemLogradouro);

    [Fact(DisplayName = "Falha ao ler a entrada do cache: resolve do banco e grava a entrada")]
    public async Task FalhaAoLerAEntrada_ResolveDoBanco()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto resolvido = Endereco();
        cenario.ComSeloVigente(Selo);
        cenario.Cache.ObterAsync<CepResolvidoDto>(ChaveCep, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha ao ler a entrada"));
        cenario.ComResolucaoNoBanco(Cep, resolvido);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(resolvido);
        await cenario.Reader.Received(1).ResolverAsync(Cep, Arg.Any<CancellationToken>());
        await cenario.Cache.Received(1).DefinirAsync(ChaveCep, resolvido, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Falha ao ler o selo de versão: resolve do banco sem ler nem gravar a entrada")]
    public async Task FalhaAoLerOSelo_ResolveDoBancoSemCache()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto resolvido = Endereco();
        cenario.Cache.ObterAsync<string>(RedisGeoCepCacheInvalidador.ChaveSeloVersaoVigente, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha ao ler o selo"));
        cenario.ComResolucaoNoBanco(Cep, resolvido);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(resolvido);
        await cenario.Reader.Received(1).ResolverAsync(Cep, Arg.Any<CancellationToken>());
        await cenario.Cache.DidNotReceive().ObterAsync<CepResolvidoDto>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cenario.Cache.DidNotReceive().DefinirAsync(Arg.Any<string>(), Arg.Any<CepResolvidoDto>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Falha ao gravar a entrada no cache: a resolução do banco é devolvida sem erro")]
    public async Task FalhaAoGravarAEntrada_DevolveAResolucaoDoBanco()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto resolvido = Endereco();
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, (CepResolvidoDto?)null);
        cenario.ComResolucaoNoBanco(Cep, resolvido);
        cenario.Cache.DefinirAsync(ChaveCep, resolvido, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha ao gravar a entrada"));

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(resolvido);
        await cenario.Cache.Received(1).DefinirAsync(ChaveCep, resolvido, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Cancelamento ao ler a entrada do cache não é engolido nem vai ao banco")]
    public async Task CancelamentoAoLerAEntrada_Propaga()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        cenario.ComSeloVigente(Selo);
        cenario.Cache.ObterAsync<CepResolvidoDto>(ChaveCep, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> resolver = () => cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        await resolver.Should().ThrowAsync<OperationCanceledException>();
        await cenario.Reader.DidNotReceive().ResolverAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
