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
        ApiKeyGuard.Validate("Production", new string('a', 64));
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

    [Fact]
    public void Validate_Production_WithKeyBelowMinLength_ShouldThrowWithApiKeyNameInMessage()
    {
        var act = () => ApiKeyGuard.Validate("Production", new string('a', 31));

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ApiKey:Key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithKeyAtMinLength_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Production", new string('a', 32));
    }

    [Fact]
    public void Validate_Production_WithLongKey_ShouldNotThrow()
    {
        var openSslLikeKey = new string('a', 64);

        ApiKeyGuard.Validate("Production", openSslLikeKey);
    }

    [Fact]
    public void Validate_Development_WithShortKey_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Development", "123");
    }

    [Fact]
    public void Validate_Testing_WithShortKey_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Testing", "123");
    }

    [Fact]
    public void Validate_Production_WithKeyBelowMinLength_MessageShouldNotContainKeyValue()
    {
        const string shortKey = "123";

        var act = () => ApiKeyGuard.Validate("Production", shortKey);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.DoesNotContain(shortKey, exception.Message, StringComparison.Ordinal);
    }
}
