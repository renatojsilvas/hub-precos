using Hub.Application.Common.Interfaces;
using Hub.Application.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Hub.Infrastructure.Outbox;

[DisallowConcurrentExecution]
public sealed class RelayOutboxJob(
    ISender sender,
    ILogger<RelayOutboxJob> logger,
    IBusinessMetrics metrics,
    RelayOutboxFalhaLogThrottle falhaLogThrottle) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var result = await sender.Send(new PublicarOutboxCommand(), context.CancellationToken);

        if (result.IsSuccess)
        {
            if (falhaLogThrottle.DeveLogarRecuperacao())
            {
                logger.LogInformation("Relay outbox recuperado apos falha(s) anterior(es).");
            }

            logger.LogInformation(
                "Relay outbox concluido: Lotes={Lotes}, Publicados={Publicados}, LotePartiu={LotePartiu}, " +
                "PendentesRestantes={PendentesRestantes}, IdadeMaisAntiga={IdadeMaisAntiga}",
                result.Value.Lotes, result.Value.Publicados, result.Value.LotePartiu,
                result.Value.PendentesRestantes, result.Value.IdadeMaisAntiga);

            metrics.RecordCicloRelay("success");
            metrics.RecordEventosPublicados(result.Value.Publicados);

            if (result.Value.PendentesRestantes is { } pendentesRestantes)
            {
                metrics.RecordOutboxBacklog(pendentesRestantes, result.Value.IdadeMaisAntiga?.TotalSeconds ?? 0);
            }
        }
        else
        {
            if (falhaLogThrottle.DeveLogarFalha())
            {
                logger.LogError(
                    "Relay outbox falhou: {ErrorCode} - {ErrorDescription}",
                    result.Error.Code, result.Error.Description);
            }

            metrics.RecordCicloRelay("failure");
        }
    }
}
