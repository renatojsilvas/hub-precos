using System.Globalization;
using Hub.Domain.Common;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hub.Application.Outbox;

public sealed class PublicarOutboxCommandHandler(
    IOutboxReadRepository read,
    IEventPublisher publisher,
    IOutboxWriteRepository write,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<PublicarOutboxCommandHandler> logger)
    : IRequestHandler<PublicarOutboxCommand, Result<RelayResultado>>
{
    private const int TamanhoLotePadrao = 100;
    private const int MaxLotesPorCicloPadrao = 50;

    public async Task<Result<RelayResultado>> Handle(PublicarOutboxCommand request, CancellationToken ct)
    {
        var tamanhoLote = ResolverTamanhoLote();
        var maxLotesPorCiclo = ResolverMaxLotesPorCiclo();

        var lotes = 0;
        var publicados = 0;
        var lotePartiu = false;

        while (lotes < maxLotesPorCiclo)
        {
            var pendentesResult = await read.ObterPendentesAsync(tamanhoLote, ct);
            if (pendentesResult.IsFailure)
            {
                logger.LogError(
                    "Falha ao obter mensagens pendentes da outbox: {Code} - {Description}",
                    pendentesResult.Error.Code, pendentesResult.Error.Description);
                return pendentesResult.Error;
            }

            var lote = pendentesResult.Value;
            if (lote.Count == 0)
            {
                break;
            }

            lotes++;

            var publicarResult = await publisher.PublicarAsync(lote, ct);
            if (publicarResult.IsFailure)
            {
                logger.LogError(
                    "Lote de outbox com {Tamanho} mensagem(ns) falhou ao publicar sem nenhuma confirmação: {Code} - {Description}",
                    lote.Count, publicarResult.Error.Code, publicarResult.Error.Description);
                return publicarResult.Error;
            }

            var confirmados = publicarResult.Value;

            if (confirmados > 0)
            {
                var idsConfirmados = lote.Take(confirmados).Select(m => m.Id).ToList();

                var marcarResult = await write.MarcarPublicadosAsync(idsConfirmados, timeProvider.GetUtcNow(), ct);
                if (marcarResult.IsFailure)
                {
                    logger.LogError(
                        "Broker confirmou {Confirmados} mensagem(ns), mas falhou ao marcá-las como publicadas: " +
                        "{Code} - {Description}; mensagens serão republicadas no próximo ciclo (at-least-once).",
                        confirmados, marcarResult.Error.Code, marcarResult.Error.Description);
                    return marcarResult.Error;
                }

                var afetados = marcarResult.Value;
                if (afetados != idsConfirmados.Count)
                {
                    logger.LogWarning(
                        "Divergência ao marcar mensagens da outbox como publicadas: {Confirmados} confirmada(s) " +
                        "pelo broker, mas apenas {Afetados} marcada(s) no banco; provável concorrência com " +
                        "outra instância do relay marcando as mesmas linhas.",
                        idsConfirmados.Count, afetados);
                }

                publicados += afetados;
            }

            if (confirmados < lote.Count)
            {
                lotePartiu = true;
                logger.LogWarning(
                    "Lote de outbox publicou parcialmente: {Confirmados} de {Total} mensagem(ns) confirmada(s); " +
                    "ciclo de relay encerrado, retomada a partir do primeiro item não confirmado no próximo ciclo.",
                    confirmados, lote.Count);
                break;
            }

            if (lote.Count < tamanhoLote)
            {
                break;
            }
        }

        var backlogResult = await read.ObterBacklogAsync(ct);
        if (backlogResult.IsFailure)
        {
            logger.LogWarning(
                "Falha ao obter backlog da outbox ao final do ciclo de relay: {Code} - {Description}; " +
                "ciclo publicou normalmente, backlog reportado como desconhecido.",
                backlogResult.Error.Code, backlogResult.Error.Description);
            return new RelayResultado(lotes, publicados, lotePartiu, null, null);
        }

        return new RelayResultado(
            lotes, publicados, lotePartiu, backlogResult.Value.Pendentes, backlogResult.Value.IdadeMaisAntiga);
    }

    private int ResolverTamanhoLote()
    {
        var configurado = configuration["Outbox:Relay:TamanhoLote"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return TamanhoLotePadrao;
        }

        if (!int.TryParse(configurado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tamanho)
            || tamanho < 1)
        {
            logger.LogWarning(
                "Outbox:Relay:TamanhoLote configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, TamanhoLotePadrao);
            return TamanhoLotePadrao;
        }

        return tamanho;
    }

    private int ResolverMaxLotesPorCiclo()
    {
        var configurado = configuration["Outbox:Relay:MaxLotesPorCiclo"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return MaxLotesPorCicloPadrao;
        }

        if (!int.TryParse(configurado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLotes)
            || maxLotes < 1)
        {
            logger.LogWarning(
                "Outbox:Relay:MaxLotesPorCiclo configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, MaxLotesPorCicloPadrao);
            return MaxLotesPorCicloPadrao;
        }

        return maxLotes;
    }
}
