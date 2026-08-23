namespace Hub.Application.Precos;

public sealed record AsOfCampo(string Campo, decimal Valor, string Fonte, int Revisao, DateTimeOffset ObservadoEm, DateOnly DataRef);
