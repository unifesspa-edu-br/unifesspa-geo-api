namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

internal sealed record DescricaoDeErro(
    string? Codigo,
    IReadOnlyList<string?>? Requisitos,
    IReadOnlyList<string?>? OQueAconteceu,
    IReadOnlyList<string?>? ComoResolver,
    IReadOnlyList<string?>? OndeAparece = null);
