namespace Hub.Application.Precos;

public sealed record AsOfInstrumento(string InstrumentoId, bool Existe, DateOnly? DataRef, IReadOnlyList<AsOfCampo> Campos);
