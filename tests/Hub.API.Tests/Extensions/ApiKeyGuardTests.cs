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

    public static IEnumerable<object[]> BlockedPlaceholderSeparatorVariants()
    {
        string[] blockedTerms =
        {
            "CHANGE-ME-IN-PRODUCTION",
            "dev-local-key",
            "uma-chave-qualquer-para-dev",
        };

        char[] separators = ['-', '_', '.'];

        foreach (var term in blockedTerms)
        {
            foreach (var separator in separators)
            {
                yield return new object[] { term.Replace('-', separator) };
            }
        }
    }

    [Theory]
    [MemberData(nameof(BlockedPlaceholderSeparatorVariants))]
    public void Validate_Production_WithBlockedPlaceholderSeparatorVariantPaddedAboveMinLength_ShouldThrowForBlockedReason(
        string blockedPlaceholderVariant)
    {
        var padded = blockedPlaceholderVariant + new string('x', 40 - blockedPlaceholderVariant.Length);
        Assert.True(padded.Length >= 32, "O padding precisa deixar a chave com 32+ caracteres.");

        var act = () => ApiKeyGuard.Validate("Production", padded);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("placeholder conhecido", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abaixo do mínimo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithBlockedPlaceholderDifferentCase_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "change-me-in-production");

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("placeholder conhecido", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithBlockedPlaceholderSurroundedByWhitespace_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "   dev-local-key   ");

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("placeholder conhecido", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithWhitespacePaddedShortKey_ShouldThrowForLengthReason()
    {
        var whitespacePaddedKey = new string(' ', 30) + "ab";
        Assert.Equal(32, whitespacePaddedKey.Length);

        var act = () => ApiKeyGuard.Validate("Production", whitespacePaddedKey);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("abaixo do mínimo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Production_WithLegitimateStrongKey_ShouldNotThrow()
    {
        ApiKeyGuard.Validate("Production", new string('a', 64));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Validate_ExemptEnvironment_WithBlockedPlaceholder_ShouldNotThrow(string environmentName)
    {
        ApiKeyGuard.Validate(environmentName, "CHANGE-ME-IN-PRODUCTION");
    }
}
