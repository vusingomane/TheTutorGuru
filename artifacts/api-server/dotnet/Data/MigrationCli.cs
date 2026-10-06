using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using System.Runtime.Versioning;
using TutorGuru.Api.Identity;

namespace TutorGuru.Api.Data;

[SupportedOSPlatform("linux")]
public static class MigrationCli
{
    private static readonly string[] Tables = ["guru_profiles", "guru_subjects", "guru_courses", "guru_learners", "guru_enrollments", "guru_sessions", "guru_materials", "guru_recordings", "guru_attendance", "guru_uploads"];
    private static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("PRIVATE_MIGRATION_DIR");
            if (!string.IsNullOrEmpty(configured)) return Path.GetFullPath(configured);
            // Locate the workspace in either Debug/Release or publish layouts;
            // a fixed number of "../" segments is wrong after publishing.
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "pnpm-workspace.yaml")))
                    return Path.Combine(directory.FullName, ".data");
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".data"));
        }
    }
    private static async Task<Dictionary<string, Dictionary<string, string>>> Snapshot(Database db)
    {
        Dictionary<string, Dictionary<string, string>> result = new();
        foreach (var table in Tables)
        {
            var key = table == "guru_uploads" ? "object_path" : "id";
            var rows = await db.Query($"SELECT {key}::text AS key,md5(row_to_json(t)::text) AS hash FROM {table} t");
            result[table] = rows.ToDictionary(r => r["key"]!.ToString()!, r => r["hash"]!.ToString()!);
        }
        return result;
    }
    public static async Task<int> Run(string[] args, Database db)
    {
        Directory.CreateDirectory(Root);
        File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (args[0] == "--migrate")
        {
            var baselinePath = Path.Combine(Root, "pre-dotnet-fingerprints.json");
            if (!File.Exists(baselinePath))
            {
                // pg_dump uses inherited environment, never putting credentials in arguments.
                var dump = Path.Combine(Root, "pre-dotnet.dump");
                var psi = new ProcessStartInfo("pg_dump") { RedirectStandardError = true };
                psi.ArgumentList.Add("--format=custom");
                psi.ArgumentList.Add("--file=" + dump);
                foreach (var (key, value) in db.BackupEnvironment) psi.Environment[key] = value;
                using var process = Process.Start(psi)!;
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidOperationException("Database backup failed; migration has not run. Check pg_dump and database access.");
                File.SetUnixFileMode(dump, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(await Snapshot(db)));
                File.SetUnixFileMode(baselinePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            await using var c = await db.Open();
            await using var tx = await c.BeginTransactionAsync();
            await c.ExecuteAsync("SELECT pg_advisory_xact_lock(94223001)", transaction: tx);
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data/identity.sql"));
            await c.ExecuteAsync(sql, transaction: tx);
            // Upgrade only newly introduced Identity key storage, never domain
            // records. Cookies remain valid because the decrypted XML is identical.
            var keys = await c.QueryAsync("SELECT name,xml FROM guru_identity_keys FOR UPDATE", transaction: tx);
            foreach (var key in keys)
            {
                string xml = key.xml;
                if (!KeyEnvelope.Encrypted(xml))
                    await c.ExecuteAsync("UPDATE guru_identity_keys SET xml=@xml WHERE name=@name", new { xml = KeyEnvelope.Protect(xml), name = (string)key.name }, tx);
            }
            await tx.CommitAsync();
            Console.WriteLine("Identity schema applied. Legacy tables unchanged. Backup retained privately in .data.");
            return 0;
        }
        if (args[0] == "--verify-preservation")
        {
            var baseline = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(await File.ReadAllTextAsync(Path.Combine(Root, "pre-dotnet-fingerprints.json")))!;
            var now = await Snapshot(db);
            var changes = baseline.Sum(t => t.Value.Count(r => !now[t.Key].TryGetValue(r.Key, out var hash) || hash != r.Value));
            Console.WriteLine($"Preservation check: {baseline.Values.Sum(t => t.Count)} original rows; {changes} changed or missing.");
            return changes == 0 ? 0 : 1;
        }
        if (args[0] == "--issue-invitation" && args.Length == 2)
        {
            var profile = await db.One("SELECT id,email FROM guru_profiles WHERE id=$1", args[1]) ?? throw new InvalidOperationException("Profile not found.");
            if (await db.One("SELECT identity_id FROM guru_identity_profile_links WHERE profile_id=$1", args[1]) is not null) throw new InvalidOperationException("Profile is already migrated.");
            var code = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
            await db.Query("INSERT INTO guru_identity_invitations(code_hash,profile_id,email,expires_at) VALUES($1,$2,$3,now()+interval '7 days')", hash, args[1], profile["email"]);
            var file = Path.Combine(Root, "invitation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(args[1])))[..16] + ".json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { profileId = args[1], email = profile["email"], code }));
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Console.WriteLine("Single-use invitation written to a private .data file. Deliver only to the verified owner; never publish this file.");
            return 0;
        }
        throw new InvalidOperationException("Supported commands: --migrate, --verify-preservation, --issue-invitation <legacy-profile-id>.");
    }
}
