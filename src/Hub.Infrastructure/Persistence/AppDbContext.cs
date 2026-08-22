using Hub.Application.Common.Interfaces;
using Hub.Domain.Instrumentos;
using Hub.Domain.Outbox;
using Hub.Domain.Precos;
using Microsoft.EntityFrameworkCore;

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

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }
}
