using Hub.Domain.Common;

namespace Hub.Application.Adapters;

public sealed record PrecoLido(int Linha, Result<PriceObserved> Preco);
