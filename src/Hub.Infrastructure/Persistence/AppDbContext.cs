using Hub.Application.Common.Interfaces;
using Hub.Domain.Common;
using Hub.Domain.Instrumentos;
using Hub.Domain.Outbox;
using Hub.Domain.Precos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hub.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Instrumento> Instrumentos => Set<Instrumento>();
    public DbSet<InstrumentoFonte> InstrumentoFontes => Set<InstrumentoFonte>();
    public DbSet<Preco> Precos => Set<Preco>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    async Task<Result> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            {
                entry.State = EntityState.Detached;
            }

            return Result.Failure(DomainErrors.General.Conflict(
                "Conflito de gravação: outra execução já persistiu um registro com a mesma chave nesta janela."));
        }
    }

    void IUnitOfWork.LimparRastreamento()
    {
        ChangeTracker.Clear();
    }
}
