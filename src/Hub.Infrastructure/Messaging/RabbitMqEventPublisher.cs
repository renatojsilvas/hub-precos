using System.Globalization;
using System.Text;
using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hub.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher(
    RabbitMqConnectionProvider connectionProvider, ILogger<RabbitMqEventPublisher> logger) : IEventPublisher
{
    public async Task<Result<int>> PublicarAsync(IReadOnlyList<OutboxPendente> lote, CancellationToken ct)
    {
        var confirmados = 0;
        IChannel? channel = null;

        try
        {
            var connection = await connectionProvider.ObterConexaoAsync(ct);

            channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                ct);

            var publicacoes = new List<Task>(lote.Count);

            foreach (var mensagem in lote)
            {
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    Type = mensagem.Tipo,
                    MessageId = mensagem.Id.ToString(CultureInfo.InvariantCulture)
                };

                var body = Encoding.UTF8.GetBytes(mensagem.Payload);

                publicacoes.Add(channel
                    .BasicPublishAsync(connectionProvider.Exchange, mensagem.RoutingKey, false, properties, body, ct)
                    .AsTask());
            }

            foreach (var publicacao in publicacoes)
            {
                await publicacao;
                confirmados++;
            }

            return Result<int>.Success(confirmados);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (confirmados == 0)
            {
                return Result<int>.Failure(OutboxErrors.BrokerIndisponivel);
            }

            logger.LogWarning(
                ex,
                "Lote de outbox publicado parcialmente no RabbitMQ: {Confirmados} de {Total} mensagem(ns) " +
                "confirmada(s) antes da falha.",
                confirmados, lote.Count);

            return Result<int>.Success(confirmados);
        }
        finally
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
    }
}
