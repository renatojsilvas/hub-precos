using Hub.Application.Common.Interfaces;
using Hub.Application.Ingestao;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Hub.Infrastructure.Ingestao;

[DisallowConcurrentExecution]
public sealed class TdIngestaoJob(ISender sender, ILogger<TdIngestaoJob> logger, IBusinessMetrics metrics) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var result = await sender.Send(new IngerirPrecosTdCommand(), context.CancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Ingestao TD API concluida: CicloEncerradoNaSonda={CicloEncerradoNaSonda}, " +
                "InstrumentosBackfill={InstrumentosBackfill}, InstrumentosDelta={InstrumentosDelta}, " +
                "InstrumentosPulados={InstrumentosPulados}, InstrumentosComFalha={InstrumentosComFalha}, " +
                "PrecosInseridos={PrecosInseridos}, PrecosRevisados={PrecosRevisados}, " +
                "PrecosInalterados={PrecosInalterados}, PrecosRejeitados={PrecosRejeitados}, " +
                "LinhasComErro={LinhasComErro}, EventosGerados={EventosGerados}, EodEmitido={EodEmitido}",
                result.Value.CicloEncerradoNaSonda, result.Value.InstrumentosBackfill,
                result.Value.InstrumentosDelta, result.Value.InstrumentosPulados,
                result.Value.InstrumentosComFalha, result.Value.PrecosInseridos,
                result.Value.PrecosRevisados, result.Value.PrecosInalterados,
                result.Value.PrecosRejeitados, result.Value.LinhasComErro,
                result.Value.EventosGerados, result.Value.EodEmitido);

            metrics.RecordCicloIngestao("success");
            metrics.RecordIngestaoSucesso();
            metrics.RecordPrecosProcessados("inserido", result.Value.PrecosInseridos);
            metrics.RecordPrecosProcessados("revisado", result.Value.PrecosRevisados);
            metrics.RecordPrecosProcessados("inalterado", result.Value.PrecosInalterados);
            metrics.RecordPrecosProcessados("rejeitado", result.Value.PrecosRejeitados);

            if (result.Value.PrecosInseridos + result.Value.PrecosRevisados > 0)
            {
                metrics.RecordPrecoNovo();
            }

            metrics.RecordInstrumentosComFalha(result.Value.InstrumentosComFalha);
        }
        else
        {
            logger.LogError(
                "Ingestao TD API falhou: {ErrorCode} - {ErrorDescription}",
                result.Error.Code, result.Error.Description);

            metrics.RecordCicloIngestao("failure");
        }
    }
}
