namespace Unifesspa.Geo.IntegrationTests.Admin;

using Unifesspa.Geo.Application.Abstractions.Authentication;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;

/// <summary>Administrador fictício autenticado.</summary>
internal sealed class UsuarioAdministradorParaTeste : IRequiredUserContext
{
    public const string Identificador = "admin-ficticio";

    public string UserId => Identificador;

    public string Name => "Administrador Fictício";

    public string Email => "admin.ficticio@exemplo.invalid";

    public IReadOnlyList<string> Roles => ["plataforma-admin"];

    public bool HasRole(string role) => Roles.Contains(role);
}

/// <summary>Links com o endereço de acompanhamento da execução, como o contrato real.</summary>
internal sealed class LinksDeImportacaoParaTeste : IResourceLinksBuilder<ImportacaoGeoDto>
{
    public static string Endereco(Guid id) => $"/api/admin/geo/importacoes/{id}";

    public IReadOnlyDictionary<string, string> Build(ImportacaoGeoDto dto) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["self"] = Endereco(dto.Id) };
}
