using System.Text.Json.Nodes;
using Microsoft.JSInterop;

namespace TutorGuru.Web.Services;

// Requests run in the browser, so HttpOnly Identity cookies stay in the browser.
public sealed class GuruApi(IJSRuntime js)
{
    public JsonObject? Profile { get; private set; }
    public string Role => Profile?["role"]?.GetValue<string>() ?? "";
    public event Action? Changed;
    public async Task<JsonNode?> Send(string path, string method = "GET", object? body = null)
    {
        var result = await js.InvokeAsync<JsonObject>("guru.request", path, method, body);
        var status = result!["status"]!.GetValue<int>();
        if (status >= 400) throw new ApiException(status, result["body"]?["error"]?.ToString() ?? "The request failed. Please try again.");
        return result["body"];
    }
    public async Task RefreshProfile()
    {
        try { Profile = (await Send("/profile"))?.AsObject(); }
        catch (ApiException e) when (e.Status is 401 or 404) { Profile = null; }
        Changed?.Invoke();
    }
    public async Task Logout() { await Send("/auth/logout", "POST"); Profile = null; Changed?.Invoke(); }
    public async Task<string> Upload(string inputId)
        => await js.InvokeAsync<string>("guru.upload", inputId);
    public static string FileUrl(string path) => "/api/storage" + path;
}
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
