using Hub.Domain.Common;

namespace Hub.Application.Common.Interfaces;

public interface IUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    void LimparRastreamento();
}
