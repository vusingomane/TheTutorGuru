using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace TutorGuru.Api.Data;

public sealed class Database
{
    private readonly NpgsqlDataSource source;
    public IReadOnlyDictionary<string, string> BackupEnvironment { get; }
    public Database()
    {
        var url = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("DATABASE_URL is required.");
        if (url.StartsWith("postgres"))
        {
            var u = new Uri(url);
            var credentials = u.UserInfo.Split(':', 2);
            var cs = new NpgsqlConnectionStringBuilder
            {
                Host = u.Host, Port = u.IsDefaultPort ? 5432 : u.Port,
                Database = u.AbsolutePath.Trim('/'),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
                SslMode = u.Query.Contains("sslmode=require") ? SslMode.Require : SslMode.Prefer,
                IncludeErrorDetail = false
            };
            url = cs.ConnectionString;
        }
        var backup = new NpgsqlConnectionStringBuilder(url);
        BackupEnvironment = new Dictionary<string, string>
        {
            ["PGHOST"] = backup.Host!, ["PGPORT"] = backup.Port.ToString(),
            ["PGDATABASE"] = backup.Database!, ["PGUSER"] = backup.Username!,
            ["PGPASSWORD"] = backup.Password ?? "", ["PGSSLMODE"] = backup.SslMode == SslMode.Require ? "require" : "prefer"
        };
        source = NpgsqlDataSource.Create(url);
    }
    public async Task<NpgsqlConnection> Open() => await source.OpenConnectionAsync();
    public async Task<List<IDictionary<string, object?>>> Query(string sql, params object?[] args)
    {
        var p = new DynamicParameters();
        for (var i = 0; i < args.Length; i++) p.Add($"p{i + 1}", args[i]);
        sql = Regex.Replace(sql, @"\$(\d+)", "@p$1");
        await using var c = await Open();
        var rows = await c.QueryAsync(sql, p);
        return rows.Select(r => (IDictionary<string, object?>)r).ToList();
    }
    public async Task<IDictionary<string, object?>?> One(string sql, params object?[] args)
        => (await Query(sql, args)).FirstOrDefault();
}
