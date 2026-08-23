using Hub.API.Extensions;

namespace Hub.API.Tests.Extensions;

public sealed class ApiKeyGuardTests
{
    [Fact]
    public void Validate_Production_WithEmptyKey_ShouldThrowWithApiKeyNameInMessage()
    {
        var act = () => ApiKeyGuard.Validate("Production", "");

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ApiKey:Key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithNullKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", null);

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validate_Production_WithWhitespaceKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "   ");

        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void Validate_Production_WithStrongKey_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Production", "uma-chave-real-forte");
    }

    [Fact]
    public void Validate_Development_WithEmptyKey_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Development", "");
    }

    [Fact]
    public void Validate_Testing_WithEmptyKey_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Testing", "");
    }

    [Fact]
    public void Validate_UnknownEnvironment_WithEmptyKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Staging", "");

        Assert.Throws<InvalidOperationException>(act);
    }
}
