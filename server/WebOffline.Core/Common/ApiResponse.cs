using System;
using System.Collections.Generic;

namespace WebOffline.Core.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();
    public int StatusCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string? message = null, int statusCode = 200)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message,
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };
    }

    public static ApiResponse<T> Fail(string error, int statusCode = 400)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Data = default,
            Errors = new List<string> { error },
            Message = error,
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };
    }

    public static ApiResponse<T> Fail(IEnumerable<string> errors, int statusCode = 400, string? message = null)
    {
        var errorList = new List<string>(errors);
        return new ApiResponse<T>
        {
            Success = false,
            Data = default,
            Errors = errorList,
            Message = message ?? (errorList.Count > 0 ? errorList[0] : "An error occurred"),
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };
    }
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null, int statusCode = 200)
    {
        return new ApiResponse
        {
            Success = true,
            Data = null,
            Message = message,
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };
    }

    public static new ApiResponse Fail(string error, int statusCode = 400)
    {
        return new ApiResponse
        {
            Success = false,
            Data = null,
            Errors = new List<string> { error },
            Message = error,
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };
    }

    public static new ApiResponse Fail(IEnumerable<string> errors, int statusCode = 400, string? message = null)
    {
        var errorList = new List<string>(errors);
        return new ApiResponse
        {
            Success = false,
            Data = null,
            Errors = errorList,
            Message = message ?? (errorList.Count > 0 ? errorList[0] : "An error occurred"),
            StatusCode = statusCode,
            Timestamp = DateTime.UtcNow
        };
    }
}
