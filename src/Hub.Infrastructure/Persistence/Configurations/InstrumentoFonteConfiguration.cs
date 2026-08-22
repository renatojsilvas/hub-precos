using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hub.Infrastructure.Persistence.Configurations;

public sealed class InstrumentoFonteConfiguration : IEntityTypeConfiguration<InstrumentoFonte>
{
    public void Configure(EntityTypeBuilder<InstrumentoFonte> builder)
    {
        builder.ToTable("instrumento_fontes");

        builder.HasKey(x => new { x.InstrumentoId, x.Fonte });

        builder.Property(x => x.InstrumentoId)
            .HasColumnName("instrumento_id")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => InstrumentoId.Create(v).Value);

        builder.Property(x => x.Fonte)
            .HasColumnName("fonte")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => Fonte.Create(v).Value);

        builder.Property(x => x.CodigoNaFonte)
            .HasColumnName("codigo_na_fonte")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => CodigoNaFonte.Create(v).Value);

        builder.HasOne<Instrumento>()
            .WithMany()
            .HasForeignKey(x => x.InstrumentoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Fonte, x.CodigoNaFonte })
            .IsUnique()
            .HasDatabaseName("ix_instrumento_fontes_fonte_codigo");
    }
}
