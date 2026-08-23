using Hub.Domain.Instrumentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hub.Infrastructure.Persistence.Configurations;

public sealed class InstrumentoConfiguration : IEntityTypeConfiguration<Instrumento>
{
    public void Configure(EntityTypeBuilder<Instrumento> builder)
    {
        builder.ToTable("instrumentos", t => t.HasCheckConstraint(
            "ck_instrumentos_classe_prefixo", "classe = split_part(id, ':', 1)"));

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .IsRequired()
            .ValueGeneratedNever()
            .HasConversion(
                v => v.Value,
                v => InstrumentoId.Create(v).Value);

        builder.Property(i => i.Classe)
            .HasColumnName("classe")
            .IsRequired()
            .HasConversion(
                v => v.Name,
                v => ClasseInstrumento.FromName(v).Value);

        builder.Property(i => i.NomeExibicao)
            .HasColumnName("nome_exibicao")
            .IsRequired();

        builder.Property(i => i.AtivoDesde)
            .HasColumnName("ativo_desde")
            .IsRequired(false);

        builder.Property(i => i.AtivoAte)
            .HasColumnName("ativo_ate")
            .IsRequired(false);

        builder.Property(i => i.PagaCupom)
            .HasColumnName("paga_cupom")
            .IsRequired();

        builder.Property(i => i.Metadados)
            .HasColumnName("metadados")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                v => v.Value,
                v => Metadados.Create(v).Value);

        builder.Property(i => i.CriadoEm)
            .HasColumnName("criado_em")
            .IsRequired();

        builder.HasIndex(i => i.Classe)
            .HasDatabaseName("ix_instrumentos_classe");
    }
}
