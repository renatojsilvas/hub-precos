using Hub.Domain.Common;

namespace Hub.Domain.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Failure_ShouldCreateFailureResult()
    {
        var error = new Error("Test.Error", "Something failed");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Success_AccessingError_ShouldThrowInvalidOperation()
    {
        var result = Result.Success();

        Assert.Throws<InvalidOperationException>(() => { _ = result.Error; });
    }

    [Fact]
    public void Failure_WithNoneError_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => { Result.Failure(Error.None); });
    }

    [Fact]
    public void Failure_WithNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => { Result.Failure(null!); });
    }

    [Fact]
    public void Success_ShouldBeAssignableToIResult()
    {
        var result = Result.Success();

        Assert.IsAssignableFrom<IResult>(result);
    }
}
