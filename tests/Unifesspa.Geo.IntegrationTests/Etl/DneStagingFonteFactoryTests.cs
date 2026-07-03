namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// <see cref="DneStagingFonteFactory"/>: resolve a connection string do banco de staging
/// dedicado (issue #12) a partir de <c>ConnectionStrings:GeoStagingDb</c> — nunca de
/// <c>GeoDb</c> (banco de domínio). Não exige Postgres real: cobre só a resolução de
/// configuração, sem abrir conexão (a fonte só conecta quando efetivamente lida).
/// </summary>
public sealed class DneStagingFonteFactoryTests
{
    private const string Versao = "202601";

    [Theory(DisplayName = "Sem ConnectionStrings:GeoStagingDb (ausente ou vazia como no appsettings.json base), lança mesmo com GeoDb presente")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SemGeoStagingDb_Lanca(string? valorGeoStagingDb)
    {
        Dictionary<string, string?> configValores = new() { ["ConnectionStrings:GeoDb"] = "Host=localhost;Database=uniplus_geo" };
        if (valorGeoStagingDb is not null)
        {
            configValores["ConnectionStrings:GeoStagingDb"] = valorGeoStagingDb;
        }

        IConfiguration configuracao = new ConfigurationBuilder().AddInMemoryCollection(configValores).Build();

        Action criar = () => _ = new DneStagingFonteFactory(configuracao, Options.Create(new EtlOpcoes()));

        criar.Should().Throw<InvalidOperationException>()
            .WithMessage("*GeoStagingDb*");
    }

    [Fact(DisplayName = "Com ConnectionStrings:GeoStagingDb presente, constrói a fonte com a versão informada")]
    public void ComGeoStagingDb_ConstroiFonte()
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GeoStagingDb"] = "Host=localhost;Database=uniplus_geo_staging",
            })
            .Build();

        DneStagingFonteFactory factory = new(configuracao, Options.Create(new EtlOpcoes()));
        IGeoFonteDados fonte = factory.Criar(Versao);

        fonte.Versao.Should().Be(Versao);
    }

    [Fact(DisplayName = "EtlOpcoes.StagingSchema tem default \"public\" — schema nativo do banco de staging dedicado")]
    public void StagingSchema_DefaultPublic()
    {
        new EtlOpcoes().StagingSchema.Should().Be("public");
    }
}
