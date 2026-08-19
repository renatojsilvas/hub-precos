using Hub.Domain.Common;

namespace Hub.Domain.Tests.Common;

public sealed class ResultGenericTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResultWithValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_ShouldCreateFailureResult()
    {
        var error = new Error("Test.Error", "Something failed");

        var result = Result<int>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Failure_AccessingValue_ShouldThrowInvalidOperation()
    {
        var error = new Error("Test.Error", "fail");
        var result = Result<int>.Failure(error);

        // Value não pode ser acessado em um resultado de falha.
        Assert.Throws<InvalidOperationException>(() => { _ = result.Value; });
    }

    [Fact]
    public void Success_AccessingError_ShouldThrowInvalidOperation()
    {
        var result = Result<int>.Success(42);

        // Error não pode ser acessado em um resultado de sucesso.
        Assert.Throws<InvalidOperationException>(() => { _ = result.Error; });
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError_ShouldCreateFailure()
    {
        var error = new Error("Test.Error", "fail");

        Result<int> result = error;

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Failure_WithNoneError_ShouldThrowArgumentException()
    {
        // Error.None não representa uma falha válida.
        Assert.Throws<ArgumentException>(() => { Result<int>.Failure(Error.None); });
    }

    [Fact]
    public void Failure_WithNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => { Result<int>.Failure(null!); });
    }

    [Fact]
    public void Success_WithNullValue_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => { Result<string>.Success(null!); });
    }

    [Fact]
    public void Success_ShouldBeAssignableToIResult()
    {
        var result = Result<int>.Success(1);

        Assert.IsAssignableFrom<IResult>(result);
    }
}
