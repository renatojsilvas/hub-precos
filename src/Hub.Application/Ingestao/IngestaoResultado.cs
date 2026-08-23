namespace Hub.Application.Ingestao;

public sealed record IngestaoResultado(
    bool CicloEncerradoNaSonda,
    int InstrumentosBackfill,
    int InstrumentosDelta,
    int InstrumentosPulados,
    int InstrumentosComFalha,
    int PrecosInseridos,
    int PrecosRevisados,
    int PrecosInalterados,
    int PrecosRejeitados,
    int LinhasComErro,
    int EventosGerados,
    DateOnly? EodEmitido);
