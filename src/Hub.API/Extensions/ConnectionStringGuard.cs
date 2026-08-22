using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Hub.API.Extensions;

public static class ConnectionStringGuard
{
    private const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";

    public static void Validate(string environmentName, string? connectionString)
    {
        if (string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString ?? string.Empty);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConnectionStringKey}' não é uma connection string Npgsql válida. {Hint}",
                ex);
        }

        if (string.IsNullOrWhiteSpace(builder.Username) || string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{ConnectionStringKey}' está sem credencial (Username/Password). {Hint}");
        }
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(environment.EnvironmentName, configuration.GetConnectionString("DefaultConnection"));

    private const string Hint =
        "Configure a credencial da role 'hub' via user-secrets: " +
        "dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" " +
        "\"Host=localhost;Port=5433;Database=hub;Username=hub;Password=<senha-da-role-hub>\" " +
        "--project src/Hub.API";
}
