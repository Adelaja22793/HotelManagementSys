using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HMS.Contracts.Auth;
using HMS.Contracts.Common;

namespace HMS.Desktop.Services;

/// <summary>
/// The only way the desktop talks to data: HTTPS calls to the central API. The workstation never connects
/// to SQL Server. Access tokens are refreshed automatically once before a call is reported as failed.
/// </summary>
public sealed class ApiClient(HttpClient http, SessionState session)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var login = await SendAsync<LoginResponse>(HttpMethod.Post, "api/v1/auth/login", new LoginRequest(email, password), authenticated: false, ct);
        session.Apply(login);
        return login;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (session.RefreshToken is { } refresh)
        {
            try { await SendAsync<object?>(HttpMethod.Post, "api/v1/auth/logout", new LogoutRequest(refresh), true, ct); }
            catch (Exception ex) when (ex is ApiException or HttpRequestException) { /* local sign-out still proceeds */ }
        }
        session.Clear();
    }

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, true, ct);

    public Task<T> PostAsync<T>(string path, object body, string? idempotencyKey = null, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Post, path, body, true, ct, idempotencyKey);

    public Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Put, path, body, true, ct);

    /// <summary>Builds a branch-scoped route for the currently selected branch.</summary>
    public string BranchPath(string relative) =>
        $"api/v1/branches/{session.Branch?.Id ?? throw new InvalidOperationException("No branch selected.")}/{relative.TrimStart('/')}";

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken ct, string? idempotencyKey = null)
    {
        var response = await SendOnceAsync(method, path, body, authenticated, idempotencyKey, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized && authenticated && await TryRefreshAsync(ct))
        {
            response.Dispose();
            response = await SendOnceAsync(method, path, body, authenticated, idempotencyKey, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                ApiError? error = null;
                try { error = await response.Content.ReadFromJsonAsync<ApiError>(ct); } catch { /* non-JSON error body */ }
                throw new ApiException(response.StatusCode, error);
            }
            if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        }
    }

    private Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, bool authenticated, string? idempotencyKey, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        if (authenticated && session.AccessToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null) request.Headers.Add(HmsHeaders.IdempotencyKey, idempotencyKey);
        request.Headers.Add(HmsHeaders.ClientVersion, typeof(ApiClient).Assembly.GetName().Version?.ToString(3));
        return http.SendAsync(request, ct);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var refresh = session.RefreshToken;
        if (refresh is null) return false;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (session.RefreshToken != refresh) return true; // another call already refreshed
            using var response = await http.PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequest(refresh), ct);
            if (!response.IsSuccessStatusCode)
            {
                session.Clear();
                return false;
            }
            session.Apply((await response.Content.ReadFromJsonAsync<LoginResponse>(ct))!);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
