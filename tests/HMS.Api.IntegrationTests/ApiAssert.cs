using System.Net;
using System.Net.Http.Json;
using HMS.Contracts.Common;

namespace HMS.Api.IntegrationTests;

internal static class ApiAssert
{
    public static async Task<ApiError> ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.NotNull(error);
        Assert.False(string.IsNullOrEmpty(error.TraceId));
        if (code is not null) Assert.Equal(code, error.Code);
        return error;
    }

    public static async Task<T> OkAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (response.StatusCode != status)
            Assert.Fail($"Expected {status} but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
