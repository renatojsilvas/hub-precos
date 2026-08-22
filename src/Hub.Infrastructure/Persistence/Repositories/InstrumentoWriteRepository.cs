using Hub.Application.Instrumentos;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Microsoft.EntityFrameworkCore;

namespace Hub.Infrastructure.Persistence.Repositories;

public sealed class InstrumentoWriteRepository(AppDbContext dbContext) : IInstrumentoWriteRepository
{
    public async Task<Result<IReadOnlyList<Instrumento>>> ListarPorClasseAsync(ClasseInstrumento classe, CancellationToken ct)
    {
        var instrumentos = await dbContext.Instrumentos
            .Where(i => i.Classe == classe)
            .ToListAsync(ct);

        return Result<IReadOnlyList<Instrumento>>.Success(instrumentos);
    }

    public async Task<Result<IReadOnlyList<InstrumentoFonte>>> ListarFontesAsync(Fonte fonte, CancellationToken ct)
    {
        var fontes = await dbContext.InstrumentoFontes
            .Where(f => f.Fonte == fonte)
            .ToListAsync(ct);

        return Result<IReadOnlyList<InstrumentoFonte>>.Success(fontes);
    }

    public async Task<Result> AdicionarAsync(Instrumento instrumento, CancellationToken ct)
    {
        await dbContext.Instrumentos.AddAsync(instrumento, ct);
        return Result.Success();
    }

    public async Task<Result> AdicionarFonteAsync(InstrumentoFonte fonte, CancellationToken ct)
    {
        await dbContext.InstrumentoFontes.AddAsync(fonte, ct);
        return Result.Success();
    }
}
