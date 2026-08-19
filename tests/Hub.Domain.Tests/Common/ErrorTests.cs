using Hub.Domain.Common;

namespace Hub.Domain.Tests.Common;

public sealed class ErrorTests
{
    [Fact]
    public void Error_ShouldHaveCodeAndDescription()
    {
        var error = new Error("Test.Error", "Something went wrong");

        Assert.Equal("Test.Error", error.Code);
        Assert.Equal("Something went wrong", error.Description);
    }

    [Fact]
    public void Errors_WithSameCodeAndDescription_ShouldBeEqual()
    {
        var error1 = new Error("Test.Error", "desc");
        var error2 = new Error("Test.Error", "desc");

        Assert.Equal(error2, error1);
    }

    [Fact]
    public void Errors_WithDifferentCode_ShouldNotBeEqual()
    {
        var error1 = new Error("Test.Error1", "desc");
        var error2 = new Error("Test.Error2", "desc");

        Assert.NotEqual(error2, error1);
    }

    [Fact]
    public void None_ShouldHaveEmptyCodeAndDescription()
    {
        var none = Error.None;

        Assert.Empty(none.Code);
        Assert.Empty(none.Description);
    }
}
