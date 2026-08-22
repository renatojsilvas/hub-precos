using Hub.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hub.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(o => o.Tipo)
            .HasColumnName("tipo")
            .IsRequired();

        builder.Property(o => o.RoutingKey)
            .HasColumnName("routing_key")
            .IsRequired();

        builder.Property(o => o.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(o => o.CriadoEm)
            .HasColumnName("criado_em")
            .IsRequired();

        builder.Property(o => o.PublicadoEm)
            .HasColumnName("publicado_em")
            .IsRequired(false);

        builder.HasIndex(o => o.Id)
            .HasFilter("publicado_em IS NULL")
            .HasDatabaseName("ix_outbox_pendentes");
    }
}
