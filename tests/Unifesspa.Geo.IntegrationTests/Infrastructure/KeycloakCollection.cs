namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.IntegrationTests.Fixtures.Hosting;

[CollectionDefinition(KeycloakContainerFixture.CollectionName, DisableParallelization = true)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit ICollectionFixture<T> exige tipo público para a definição da coleção.")]
public sealed class KeycloakCollectionDefinition : ICollectionFixture<KeycloakContainerFixture>;
