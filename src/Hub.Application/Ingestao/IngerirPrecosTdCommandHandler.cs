using System.Globalization;
using Hub.Application.Adapters;
using Hub.Application.Common.Interfaces;
using Hub.Application.Eventos;
using Hub.Application.Outbox;
using Hub.Application.Precos;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Hub.Domain.Precos;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hub.Application.Ingestao;

public sealed class IngerirPrecosTdCommandHandler(
    IPriceSourceAdapter adapter,
    IIngestaoReadRepository read,
    IPrecoWriteRepository precoWriteRepository,
    IOutboxWriteRepository outboxWriteRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<IngerirPrecosTdCommandHandler> logger)
    : IRequestHandler<IngerirPrecosTdCommand, Result<IngestaoResultado>>
{
    private const string FonteTdApi = "td-api";
    private const string ClasseTd = "td";
    private const string DataFormat = "yyyy-MM-dd";

    private const int JanelaDeltaDiasPadrao = 5;
    private const int TamanhoLotePadrao = 1000;

    private static readonly DateOnly PisoProgramaPadrao = new(2002, 1, 7);
    private static readonly IReadOnlyList<string> ClassesEod = ["td"];

    public async Task<Result<IngestaoResultado>> Handle(IngerirPrecosTdCommand request, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var discovery = await adapter.DiscoverAsync(ct);
        if (discovery.IsFailure)
        {
            logger.LogError(
                "Descoberta de instrumentos TD falhou: {Code} - {Description}",
                discovery.Error.Code, discovery.Error.Description);
            return discovery.Error;
        }

        if (!discovery.Value.FonteTemNovidade)
        {
            var existeSemPrecoResult = await read.ExisteInstrumentoSemPrecoAsync(FonteTdApi, ClasseTd, ct);
            if (existeSemPrecoResult.IsFailure)
            {
                logger.LogError(
                    "Falha ao verificar instrumentos sem preço: {Code} - {Description}",
                    existeSemPrecoResult.Error.Code, existeSemPrecoResult.Error.Description);
                return existeSemPrecoResult.Error;
            }

            if (!existeSemPrecoResult.Value)
            {
                logger.LogInformation(
                    "Ciclo de ingestão TD encerrado na sonda: fonte sem novidade e nenhum instrumento pendente de backfill.");
                return new IngestaoResultado(
                    CicloEncerradoNaSonda: true,
                    InstrumentosBackfill: 0,
                    InstrumentosDelta: 0,
                    InstrumentosPulados: 0,
                    InstrumentosComFalha: 0,
                    PrecosInseridos: 0,
                    PrecosRevisados: 0,
                    PrecosInalterados: 0,
                    PrecosRejeitados: 0,
                    LinhasComErro: 0,
                    EventosGerados: 0,
                    EodEmitido: null);
            }
        }

        var watermarksResult = await read.ObterWatermarksAsync(FonteTdApi, ClasseTd, ct);
        if (watermarksResult.IsFailure)
        {
            logger.LogError(
                "Falha ao obter watermarks TD: {Code} - {Description}",
                watermarksResult.Error.Code, watermarksResult.Error.Description);
            return watermarksResult.Error;
        }

        var piso = ResolverPisoPrograma();
        var janelaDelta = ResolverJanelaDeltaDias();
        var tamanhoLote = ResolverTamanhoLote();

        var instrumentosBackfill = 0;
        var instrumentosDelta = 0;
        var instrumentosPulados = 0;
        var instrumentosComFalha = 0;
        var precosInseridos = 0;
        var precosRevisados = 0;
        var precosInalterados = 0;
        var precosRejeitados = 0;
        var linhasComErro = 0;
        var eventosGerados = 0;

        var precosPendentes = new List<Preco>();
        var eventosPendentes = new List<OutboxMessage>();

        async Task<Result> FlushAsync()
        {
            if (precosPendentes.Count == 0 && eventosPendentes.Count == 0)
            {
                return Result.Success();
            }

            var adicionarPrecosResult = await precoWriteRepository.AdicionarRangeAsync(precosPendentes, ct);
            if (adicionarPrecosResult.IsFailure)
            {
                precosPendentes.Clear();
                eventosPendentes.Clear();
                unitOfWork.LimparRastreamento();
                return adicionarPrecosResult;
            }

            var adicionarEventosResult = await outboxWriteRepository.AdicionarRangeAsync(eventosPendentes, ct);
            if (adicionarEventosResult.IsFailure)
            {
                precosPendentes.Clear();
                eventosPendentes.Clear();
                unitOfWork.LimparRastreamento();
                return adicionarEventosResult;
            }

            var saveResult = await unitOfWork.SaveChangesAsync(ct);
            unitOfWork.LimparRastreamento();

            precosPendentes.Clear();
            eventosPendentes.Clear();

            return saveResult;
        }

        void LogFalhaDeGravacao(string instrumentoId, Error error) =>
            logger.LogWarning(
                "Falha ao gravar lote de preços/eventos para {InstrumentoId}: {Code} - {Description}; " +
                "instrumento pulado neste ciclo, watermark cuida da retomada.",
                instrumentoId, error.Code, error.Description);

        foreach (var wm in watermarksResult.Value)
        {
            if (ct.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Ciclo de ingestão TD cancelado antes de processar {InstrumentoId}.", wm.InstrumentoId);
                break;
            }

            DateOnly dataInicio;
            if (wm.Watermark is null)
            {
                dataInicio = piso;
                instrumentosBackfill++;
            }
            else if (wm.AtivoAte is DateOnly ativoAte && ativoAte < hoje)
            {
                instrumentosPulados++;
                continue;
            }
            else
            {
                var pisoDelta = piso.AddDays(1);
                var candidato = wm.Watermark.Value.AddDays(-janelaDelta);
                dataInicio = candidato < pisoDelta ? pisoDelta : candidato;
                instrumentosDelta++;
            }

            var revisoesResult = await read.ObterRevisoesCorrentesAsync(wm.InstrumentoId, FonteTdApi, dataInicio, ct);
            if (revisoesResult.IsFailure)
            {
                instrumentosComFalha++;
                logger.LogWarning(
                    "Falha ao obter revisões correntes para {InstrumentoId}: {Code} - {Description}; " +
                    "instrumento pulado neste ciclo, watermark cuida da retomada.",
                    wm.InstrumentoId, revisoesResult.Error.Code, revisoesResult.Error.Description);
                continue;
            }

            var revisoes = new Dictionary<(DateOnly DataRef, string Campo), RevisaoCorrente>(revisoesResult.Value);
            var instrumentoTruncado = false;
            var falhaDeGravacao = false;

            try
            {
                await foreach (var lido in adapter.FetchAsync(wm.CodigoNaFonte, dataInicio, ct))
                {
                    if (lido.Preco.IsFailure)
                    {
                        linhasComErro++;
                        logger.LogWarning(
                            "Linha rejeitada na ingestão TD para {InstrumentoId} (linha {Linha}): {Code} - {Description}",
                            wm.InstrumentoId, lido.Linha, lido.Preco.Error.Code, lido.Preco.Error.Description);

                        if (lido.Preco.Error.Code == AdapterErrors.TdApiHttpError.Code
                            || lido.Preco.Error.Code == AdapterErrors.TdApiRespostaInvalida.Code)
                        {
                            instrumentoTruncado = true;
                        }

                        continue;
                    }

                    var po = lido.Preco.Value;
                    var valor = Math.Round(po.Valor, 6, MidpointRounding.ToEven);
                    var chave = (po.DataRef.Value, po.Campo.Value);

                    int revisao;
                    if (!revisoes.TryGetValue(chave, out var corrente))
                    {
                        revisao = 0;
                    }
                    else if (corrente.Valor == valor)
                    {
                        precosInalterados++;
                        continue;
                    }
                    else
                    {
                        revisao = corrente.Revisao + 1;
                    }

                    var precoResult = Preco.Create(
                        po.InstrumentoId, po.DataRef, po.Campo, po.Fonte, valor, revisao, po.ObservadoEm);

                    if (precoResult.IsFailure)
                    {
                        precosRejeitados++;
                        logger.LogWarning(
                            "Preço rejeitado para {InstrumentoId} {DataRef} {Campo}: {Description}",
                            po.InstrumentoId.Value, po.DataRef.Value, po.Campo.Value, precoResult.Error.Description);
                        continue;
                    }

                    precosPendentes.Add(precoResult.Value);
                    revisoes[chave] = new RevisaoCorrente(revisao, valor);

                    if (revisao > 0)
                    {
                        precosRevisados++;
                    }
                    else
                    {
                        precosInseridos++;
                    }

                    var emitir = revisao > 0
                        || (wm.Watermark is not null && po.DataRef.Value >= hoje.AddDays(-janelaDelta));

                    if (emitir)
                    {
                        var eventoOutbox = EventosFactory.ParaPrecoObservado(po, revisao);
                        var mensagemResult = OutboxMessage.Create(
                            eventoOutbox.Tipo, eventoOutbox.RoutingKey, eventoOutbox.Payload, timeProvider.GetUtcNow());

                        if (mensagemResult.IsFailure)
                        {
                            logger.LogWarning(
                                "Falha ao criar mensagem de outbox para {InstrumentoId} {DataRef} {Campo}: {Description}",
                                po.InstrumentoId.Value, po.DataRef.Value, po.Campo.Value, mensagemResult.Error.Description);
                        }
                        else
                        {
                            eventosPendentes.Add(mensagemResult.Value);
                            eventosGerados++;
                        }
                    }

                    if (precosPendentes.Count >= tamanhoLote)
                    {
                        var flushResult = await FlushAsync();
                        if (flushResult.IsFailure)
                        {
                            LogFalhaDeGravacao(wm.InstrumentoId, flushResult.Error);
                            falhaDeGravacao = true;
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Ciclo de ingestão TD cancelado durante o processamento de {InstrumentoId}; " +
                    "lote pendente descartado, dados já commitados preservados.",
                    wm.InstrumentoId);
                break;
            }

            if (!falhaDeGravacao)
            {
                var flushFinalResult = await FlushAsync();
                if (flushFinalResult.IsFailure)
                {
                    LogFalhaDeGravacao(wm.InstrumentoId, flushFinalResult.Error);
                    falhaDeGravacao = true;
                }
            }

            if (instrumentoTruncado || falhaDeGravacao)
            {
                instrumentosComFalha++;
            }
        }

        DateOnly? eodEmitido = null;

        if (!ct.IsCancellationRequested)
        {
            var eodResult = await read.ObterDataEodAsync(FonteTdApi, ClasseTd, hoje, ct);
            if (eodResult.IsFailure)
            {
                logger.LogWarning(
                    "Falha ao obter data de fechamento EOD: {Code} - {Description}; " +
                    "EOD não emitido neste ciclo, sai no próximo.",
                    eodResult.Error.Code, eodResult.Error.Description);
            }
            else
            {
                var eod = eodResult.Value;
                if (eod.Fechado is DateOnly fechado && (eod.UltimoEmitido is null || fechado > eod.UltimoEmitido))
                {
                    if (eod.AtivosSemPreco > 0)
                    {
                        logger.LogWarning(
                            "EodPricesReady para {Fechado} está sendo anunciado com {AtivosSemPreco} " +
                            "instrumento(s) ativo(s) da classe {Classe} sem preço algum recebido até agora.",
                            fechado, eod.AtivosSemPreco, ClasseTd);
                    }

                    var eventoEod = EventosFactory.ParaEodPricesReady(fechado, ClassesEod);
                    var mensagemResult = OutboxMessage.Create(
                        eventoEod.Tipo, eventoEod.RoutingKey, eventoEod.Payload, timeProvider.GetUtcNow());

                    if (mensagemResult.IsFailure)
                    {
                        logger.LogWarning(
                            "Falha ao criar mensagem de outbox para EodPricesReady {Fechado}: {Description}",
                            fechado, mensagemResult.Error.Description);
                    }
                    else
                    {
                        var adicionarResult = await outboxWriteRepository.AdicionarEodSeAusenteAsync(mensagemResult.Value, ct);
                        if (adicionarResult.IsSuccess)
                        {
                            eodEmitido = fechado;
                            eventosGerados++;
                        }
                        else
                        {
                            logger.LogInformation(
                                "EodPricesReady para {Fechado} já foi emitido por outra execução: {Description}",
                                fechado, adicionarResult.Error.Description);
                        }
                    }
                }
            }
        }

        logger.LogInformation(
            "Ciclo de ingestão TD concluído: backfill={InstrumentosBackfill}, delta={InstrumentosDelta}, " +
            "pulados={InstrumentosPulados}, comFalha={InstrumentosComFalha}, inseridos={PrecosInseridos}, " +
            "revisados={PrecosRevisados}, inalterados={PrecosInalterados}, rejeitados={PrecosRejeitados}, " +
            "linhasComErro={LinhasComErro}, eventos={EventosGerados}, eod={EodEmitido}",
            instrumentosBackfill, instrumentosDelta, instrumentosPulados, instrumentosComFalha,
            precosInseridos, precosRevisados, precosInalterados, precosRejeitados, linhasComErro,
            eventosGerados, eodEmitido);

        return new IngestaoResultado(
            CicloEncerradoNaSonda: false,
            InstrumentosBackfill: instrumentosBackfill,
            InstrumentosDelta: instrumentosDelta,
            InstrumentosPulados: instrumentosPulados,
            InstrumentosComFalha: instrumentosComFalha,
            PrecosInseridos: precosInseridos,
            PrecosRevisados: precosRevisados,
            PrecosInalterados: precosInalterados,
            PrecosRejeitados: precosRejeitados,
            LinhasComErro: linhasComErro,
            EventosGerados: eventosGerados,
            EodEmitido: eodEmitido);
    }

    private DateOnly ResolverPisoPrograma()
    {
        var configurado = configuration["TdApi:PisoPrograma"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return PisoProgramaPadrao;
        }

        if (!DateOnly.TryParseExact(
            configurado, DataFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var piso))
        {
            logger.LogWarning(
                "TdApi:PisoPrograma configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, PisoProgramaPadrao);
            return PisoProgramaPadrao;
        }

        return piso;
    }

    private int ResolverJanelaDeltaDias()
    {
        var configurado = configuration["TdApi:JanelaDeltaDias"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return JanelaDeltaDiasPadrao;
        }

        if (!int.TryParse(configurado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var janela)
            || janela < 1)
        {
            logger.LogWarning(
                "TdApi:JanelaDeltaDias configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, JanelaDeltaDiasPadrao);
            return JanelaDeltaDiasPadrao;
        }

        return janela;
    }

    private int ResolverTamanhoLote()
    {
        var configurado = configuration["TdApi:TamanhoLote"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return TamanhoLotePadrao;
        }

        if (!int.TryParse(configurado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tamanho)
            || tamanho < 1)
        {
            logger.LogWarning(
                "TdApi:TamanhoLote configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, TamanhoLotePadrao);
            return TamanhoLotePadrao;
        }

        return tamanho;
    }
}
