using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hub.API.Tests.Extensions;

public sealed class ApiKeyGuardBootTests
{
    [Fact]
    public void Boot_Production_WithEmptyApiKey_ShouldFailToStartWithMessageContainingApiKeyName()
    {
        using var factory = new ProductionWithoutApiKeyFactory();

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("ApiKey:Key", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ProductionWithoutApiKeyFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=fake;Username=fake;Password=fake",
                    ["ApiKey:Key"] = ""
                });
            });
        }
    }
}
