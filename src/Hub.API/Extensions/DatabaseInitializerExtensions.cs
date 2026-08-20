namespace Hub.API.Extensions;

public static class DatabaseInitializerExtensions
{
    public static Task InitializeDatabaseAsync(this WebApplication app) =>
        app.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
}
