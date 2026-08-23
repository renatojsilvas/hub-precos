using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hub.API.Extensions;

public static class ApiKeyGuard
{
    private const string ConfigKey = "ApiKey:Key";
    private const int MinKeyLength = 32;

    private static readonly char[] SeparatorChars = ['-', '_', '.', ' '];

    private static readonly string[] BlockedKeys =
    {
        "CHANGE-ME-IN-PRODUCTION",
        "dev-local-key",
        "uma-chave-qualquer-para-dev",
    };

    private static readonly string[] BlockedKeysCanonical =
        BlockedKeys.Select(RemoveSeparators).ToArray();

    public static void Validate(string environmentName, string? configuredKey)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trimmedKey = configuredKey?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConfigKey}' está vazia em ambiente '{environmentName}'. {Hint}");
        }

        var canonicalKey = RemoveSeparators(trimmedKey);

        if (BlockedKeysCanonical.Any(blocked => canonicalKey.Contains(blocked, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConfigKey}' contém um placeholder conhecido em ambiente " +
                $"'{environmentName}', mesmo que como parte de um valor maior ou com separadores " +
                $"diferentes (-, _, ., espaço). Placeholders proibidos: {string.Join(", ", BlockedKeys)}. {Hint}");
        }

        if (trimmedKey.Length < MinKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConfigKey}' tem {trimmedKey.Length} caracteres em ambiente " +
                $"'{environmentName}', abaixo do mínimo de {MinKeyLength}. {Hint} Para gerar uma chave forte: " +
                "openssl rand -hex 32.");
        }
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(environment.EnvironmentName, configuration[ConfigKey]);

    private static string RemoveSeparators(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (Array.IndexOf(SeparatorChars, c) < 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private const string Hint =
        "Configure a chave que o Hub exige de quem o chama via variável de ambiente ApiKey__Key " +
        "(Docker/produção) ou via dotnet user-secrets set \"ApiKey:Key\" \"<chave>\" --project src/Hub.API " +
        "(dev local). Não confundir com TdApi:ApiKey, que é a chave que o Hub ENVIA à TD API — direção oposta.";
}
