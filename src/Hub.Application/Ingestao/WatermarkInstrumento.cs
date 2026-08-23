namespace Hub.Application.Ingestao;

public sealed record WatermarkInstrumento(string InstrumentoId, string CodigoNaFonte, DateOnly? Watermark, DateOnly? AtivoAte);
