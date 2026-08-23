using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hub.API.Extensions;

public static class ApiKeyGuard
{
    private const string ConfigKey = "ApiKey:Key";

    public static void Validate(string environmentName, string? configuredKey)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConfigKey}' está vazia em ambiente '{environmentName}'. {Hint}");
        }
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(environment.EnvironmentName, configuration[ConfigKey]);

    private const string Hint =
        "Configure a chave que o Hub exige de quem o chama via variável de ambiente ApiKey__Key " +
        "(Docker/produção) ou via dotnet user-secrets set \"ApiKey:Key\" \"<chave>\" --project src/Hub.API " +
        "(dev local). Não confundir com TdApi:ApiKey, que é a chave que o Hub ENVIA à TD API — direção oposta.";
}
