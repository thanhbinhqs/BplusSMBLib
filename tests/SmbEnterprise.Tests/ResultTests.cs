using SmbEnterprise.Core.Results;

namespace SmbEnterprise.Tests;

public class ResultTests
{
    [Fact]
    public void Success_ReturnsSuccessfulResult()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_ReturnsFailureResult()
    {
        var error = new SmbError(ErrorCode.ConnectionFailed, "Connection refused");
        var result = Result<int>.Fail(error);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ConnectionFailed, result.Error.Code);
        Assert.Equal("Connection refused", result.Error.Message);
    }

    [Fact]
    public void Value_OnFailure_ThrowsInvalidOperation()
    {
        var result = Result<int>.Fail(new SmbError(ErrorCode.Unknown, "Oops"));

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Theory]
    [InlineData(ErrorCode.ConnectionFailed, true)]
    [InlineData(ErrorCode.AuthenticationFailed, false)]
    [InlineData(ErrorCode.Timeout, true)]
    [InlineData(ErrorCode.AccessDenied, false)]
    public void IsRetryable_ErrorCodes_CorrectClassification(ErrorCode code, bool expected)
    {
        Assert.Equal(expected, code.IsRetryable());
    }
}
