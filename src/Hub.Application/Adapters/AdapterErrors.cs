using Hub.Domain.Common;

namespace Hub.Application.Adapters;

public static class AdapterErrors
{
    public static readonly Error TdApiUrlNaoConfigurada =
        new("TdApi.UrlNaoConfigurada", "TdApi:BaseUrl is not configured or is not a valid absolute URL.", ErrorType.Validation);

    public static readonly Error TdApiHttpError =
        new("TdApi.HttpError", "Failed to fetch data from TD API.", ErrorType.Validation);

    public static readonly Error TdApiRespostaInvalida =
        new("TdApi.RespostaInvalida", "TD API response could not be deserialized.", ErrorType.Validation);

    public static readonly Error TdApiDataBaseInvalida =
        new("TdApi.DataBaseInvalida", "TD API preco has an unparseable dataBase.", ErrorType.Validation);
}
