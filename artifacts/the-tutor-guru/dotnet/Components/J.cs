using System.Text.Json.Nodes;
namespace TutorGuru.Web.Components;
public static class J
{
    public static string S(this JsonNode? n, string k) => n?[k]?.ToString() ?? "";
    public static int I(this JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : 0;
    public static double D(this JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
    public static bool B(this JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public static List<JsonNode> L(this JsonNode? n) => n is JsonArray a ? a.Where(x => x != null).Select(x => x!).ToList() : new();
    public static List<JsonNode> L(this JsonNode? n, string k) => n?[k].L() ?? new();
    public static string Date(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime().AddHours(2).ToString("ddd d MMM yyyy, HH:mm") + " SAST" : iso;
    public static string Size(double b) => b > 1048576 ? $"{b / 1048576:0.0} MB" : $"{b / 1024:0} KB";
    public static string ToIso(string local) => new DateTimeOffset(DateTime.Parse(local), TimeSpan.FromHours(2)).UtcDateTime.ToString("o");
    public static string ToLocalInput(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime().AddHours(2).ToString("yyyy-MM-ddTHH:mm") : "";
    public static bool Https(string? u) => string.IsNullOrWhiteSpace(u) || (Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == "https");
}
