namespace Hub.Infrastructure.TdApi;

public sealed record TituloResponse(
    string TipoTitulo, string DataVencimento, string Indexador,
    bool PagaJurosSemestrais, bool Vencido, string Codigo);

public sealed record PrecoTaxaResponse(
    string DataBase, decimal? TaxaCompra, decimal? TaxaVenda,
    decimal? PuCompra, decimal? PuVenda, decimal? PuBase);

public sealed record TitulosResponse(bool NaoModificado, IReadOnlyList<TituloResponse> Titulos);
