namespace Unifesspa.Geo.Application.Abstractions.Interfaces;

public interface IUnitOfWork
{
    Task<int> SalvarAlteracoesAsync(CancellationToken cancellationToken = default);
}
