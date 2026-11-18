using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace TutorGuru.Api.Storage;

public sealed class ObjectStorage(IHttpClientFactory clients)
{
    private const string Sidecar = "http://127.0.0.1:1106/object-storage/signed-object-url";
    private static (string Bucket, string Name) Resolve(string path)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^/objects/uploads/[a-f0-9-]{36}$")) throw new HttpError(404, "File not found.");
        var root = Environment.GetEnvironmentVariable("PRIVATE_OBJECT_DIR") ?? throw new HttpError(503, "Private file storage is not configured.");
        var parts = root.Trim('/').Split('/', 2);
        if (parts.Length != 2) throw new HttpError(503, "Private file storage is not configured correctly.");
        return (parts[0], parts[1].TrimEnd('/') + path["/objects".Length..]);
    }
    public async Task<string> Sign(string path, string method)
    {
        var (bucket, name) = Resolve(path);
        // Use UTC "Z" dates: the sidecar's date decoder rejects the escaped "+"
        // in System.Text.Json's serialization of DateTimeOffset "O" strings.
        var payload = System.Text.Json.JsonSerializer.Serialize(new { bucket_name = bucket, object_name = name, method, expires_at = DateTime.UtcNow.AddMinutes(15).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var r = await clients.CreateClient("sign").PostAsync(Sidecar, content);
        if (!r.IsSuccessStatusCode) throw new HttpError(503, $"File storage is unavailable (signing status {(int)r.StatusCode}). Please try again.");
        var json = await r.Content.ReadFromJsonAsync<JsonObject>();
        return json?["signed_url"]?.GetValue<string>() ?? throw new HttpError(503, "File storage did not return an upload URL.");
    }
    public async Task<(long Size, string Type)> Metadata(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, await Sign(path, "HEAD"));
        using var response = await clients.CreateClient("sign").SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new HttpError(404, "Uploaded file not found.");
        if (!response.IsSuccessStatusCode) throw new HttpError(503, "Unable to verify the uploaded file.");
        return (response.Content.Headers.ContentLength ?? throw new HttpError(400, "The uploaded file has no size metadata."), response.Content.Headers.ContentType?.MediaType ?? "");
    }
    public async Task Download(HttpContext c, string path, string type, string name)
    {
        var (size, _) = await Metadata(path);
        long start = 0, end = size - 1;
        var range = c.Request.Headers.Range.ToString();
        if (range.Length > 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(range, @"^bytes=(\d*)-(\d*)$");
            if (!match.Success || match.Groups[1].Value == "" && match.Groups[2].Value == "") { InvalidRange(c, size); return; }
            if (match.Groups[1].Value == "")
            {
                if (!long.TryParse(match.Groups[2].Value, out var suffix) || suffix <= 0) { InvalidRange(c, size); return; }
                start = Math.Max(0, size - suffix);
            }
            else
            {
                if (!long.TryParse(match.Groups[1].Value, out start)) { InvalidRange(c, size); return; }
                if (match.Groups[2].Value.Length > 0)
                {
                    if (!long.TryParse(match.Groups[2].Value, out end)) { InvalidRange(c, size); return; }
                    end = Math.Min(size - 1, end);
                }
            }
            if (start >= size || start > end) { InvalidRange(c, size); return; }
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, await Sign(path, "GET"));
        if (range.Length > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
        using var response = await clients.CreateClient("stream").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, c.RequestAborted);
        if (!response.IsSuccessStatusCode) throw new HttpError(503, "Unable to read this file.");
        c.Response.StatusCode = range.Length > 0 ? 206 : 200;
        c.Response.ContentType = type;
        c.Response.ContentLength = size == 0 ? 0 : end - start + 1;
        c.Response.Headers.AcceptRanges = "bytes";
        c.Response.Headers.CacheControl = "private, no-store";
        c.Response.Headers.XContentTypeOptions = "nosniff";
        if (range.Length > 0) c.Response.Headers.ContentRange = $"bytes {start}-{end}/{size}";
        if (type != "video/mp4") c.Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        await response.Content.CopyToAsync(c.Response.Body, c.RequestAborted);
    }
    private static void InvalidRange(HttpContext c, long size)
    { c.Response.StatusCode = 416; c.Response.Headers.ContentRange = $"bytes */{size}"; }
}
