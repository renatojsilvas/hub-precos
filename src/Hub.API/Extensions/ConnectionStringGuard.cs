using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Hub.API.Extensions;

/// <summary>
/// Valida no boot que a connection string do Postgres tem credencial (Username/Password).
/// O appsettings.json só guarda host/porta/database de propósito — a senha da role `hub`
/// nunca é commitada (PADROES.md §6) e precisa vir de user-secrets (dev) ou da env var
/// ConnectionStrings__DefaultConnection (container/produção). Sem essa validação, o Npgsql
/// tenta autenticar com o usuário do SO e falha com um erro de autenticação confuso, que não
/// diz ao desenvolvedor novo o que fazer. Mesmo racional/forma do
/// tesouro-direto-api/src/TesouroDireto.API/Extensions/ApiKeyGuard.cs (classe estática de
/// guarda, chamada em Program.cs logo após builder.Build()).
/// </summary>
public static class ConnectionStringGuard
{
    private const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";

    // Ao contrário do ApiKeyGuard (que dispensa Development porque uma chave de API fraca em
    // dev é aceitável), aqui a credencial é exigida em TODO ambiente real: sem ela o Npgsql
    // não conecta, dev incluso. Só "Testing" é dispensado — mesmo critério que
    // DatabaseInitializer já usa neste projeto (Extensions/DatabaseInitializer.cs) para pular
    // a migration: hosts de teste de integração (WebApplicationFactory) não dependem de uma
    // credencial real configurada por fora.
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
