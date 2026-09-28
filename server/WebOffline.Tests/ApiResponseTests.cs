using System.Collections.Generic;
using WebOffline.Core.Common;
using Xunit;

namespace WebOffline.Tests;

public class ApiResponseTests
{
    [Fact]
    public void Ok_ShouldCreateSuccessfulResponseWithData()
    {
        var data = new { Id = "123", Name = "Test" };
        var response = ApiResponse<object>.Ok(data, "Success message", 200);

        Assert.True(response.Success);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("Success message", response.Message);
        Assert.Equal(data, response.Data);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void Fail_ShouldCreateFailureResponseWithSingleError()
    {
        var response = ApiResponse<string>.Fail("Resource not found", 404);

        Assert.False(response.Success);
        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Resource not found", response.Message);
        Assert.Null(response.Data);
        Assert.Single(response.Errors);
        Assert.Equal("Resource not found", response.Errors[0]);
    }

    [Fact]
    public void Fail_ShouldCreateFailureResponseWithMultipleErrors()
    {
        var errors = new List<string> { "Field 1 required", "Field 2 invalid" };
        var response = ApiResponse.Fail(errors, 422, "Validation failed");

        Assert.False(response.Success);
        Assert.Equal(422, response.StatusCode);
        Assert.Equal("Validation failed", response.Message);
        Assert.Equal(2, response.Errors.Count);
    }
}
