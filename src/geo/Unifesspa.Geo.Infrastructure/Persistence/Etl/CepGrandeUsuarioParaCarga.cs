namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>CEP exclusivo de grande usuário já validado; a chave natural é o próprio CEP.</summary>
internal readonly record struct CepGrandeUsuarioParaCarga(
    string Cep,
    NomeComFormaDeBusca Nome,
    ProvenienciaDaCarga Proveniencia)
{
    public static CepGrandeUsuarioParaCarga? De(CepGrandeUsuarioCru linha, ProvenienciaDaCarga proveniencia, ContadorTabela contador)
    {
        string? cep = ChaveCodigo(linha.Cep);
        if (cep is null)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }
        return new CepGrandeUsuarioParaCarga(cep, new NomeComFormaDeBusca(linha.Nome ?? string.Empty, linha.NomeSemAcento), proveniencia);
    }

    public Result<CepGrandeUsuario> Criar() =>
        CepGrandeUsuario.Importar(Cep, Nome, Proveniencia);

    public Result Atualizar(CepGrandeUsuario existente) =>
        existente.Atualizar(Nome, Proveniencia);
}
