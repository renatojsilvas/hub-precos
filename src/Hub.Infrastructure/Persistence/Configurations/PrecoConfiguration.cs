using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hub.Infrastructure.Persistence.Configurations;

public sealed class PrecoConfiguration : IEntityTypeConfiguration<Preco>
{
    public void Configure(EntityTypeBuilder<Preco> builder)
    {
        builder.ToTable("precos");

        builder.HasKey(p => new { p.InstrumentoId, p.DataRef, p.Campo, p.Fonte, p.Revisao });

        builder.Property(p => p.InstrumentoId)
            .HasColumnName("instrumento_id")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => InstrumentoId.Create(v).Value);

        builder.Property(p => p.DataRef)
            .HasColumnName("data_ref")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => DataRef.Create(v).Value);

        builder.Property(p => p.Campo)
            .HasColumnName("campo")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => Campo.Create(v).Value);

        builder.Property(p => p.Fonte)
            .HasColumnName("fonte")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => Fonte.Create(v).Value);

        builder.Property(p => p.Valor)
            .HasColumnName("valor")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(p => p.Revisao)
            .HasColumnName("revisao")
            .IsRequired();

        builder.Property(p => p.ObservadoEm)
            .HasColumnName("observado_em")
            .IsRequired();

        builder.HasOne<Instrumento>()
            .WithMany()
            .HasForeignKey(p => p.InstrumentoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.InstrumentoId, p.DataRef })
            .IsDescending(false, true)
            .HasDatabaseName("ix_precos_lookup");

        builder.HasIndex(p => p.ObservadoEm)
            .HasDatabaseName("ix_precos_observado_em");
    }
}
