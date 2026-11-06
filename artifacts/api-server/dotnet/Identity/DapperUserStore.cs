using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Identity;

// ASP.NET Core Identity supplies password hashing, lockout, security stamps and
// token providers. Dapper persists it, without introducing EF or changing domain IDs.
public sealed class DapperUserStore(Database db) :
    IUserPasswordStore<IdentityUser>, IUserEmailStore<IdentityUser>,
    IUserSecurityStampStore<IdentityUser>, IUserLockoutStore<IdentityUser>
{
    public void Dispose() { }
    public async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var data = JsonSerializer.SerializeToNode(user)!.AsObject();
        data["PlatformActivated"] = user.EmailConfirmed;
        await using var c = await db.Open();
        try
        {
            await c.ExecuteAsync("INSERT INTO guru_identity_users(id,normalized_email,normalized_username,concurrency_stamp,data) VALUES(@Id,@NormalizedEmail,@NormalizedUserName,@ConcurrencyStamp,CAST(@Data AS jsonb))",
                new { user.Id, user.NormalizedEmail, user.NormalizedUserName, user.ConcurrencyStamp, Data = data.ToJsonString() });
            return IdentityResult.Success;
        }
        catch (PostgresException e) when (e.SqlState == "23505")
        { return IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "An account already exists with this email." }); }
    }
    public async Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var previous = user.ConcurrencyStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        await using var c = await db.Open();
        var changed = await c.ExecuteAsync("UPDATE guru_identity_users SET normalized_email=@NormalizedEmail,normalized_username=@NormalizedUserName,concurrency_stamp=@ConcurrencyStamp,data=data || CAST(@Data AS jsonb) WHERE id=@Id AND concurrency_stamp=@Previous",
            new { user.Id, user.NormalizedEmail, user.NormalizedUserName, user.ConcurrencyStamp, Previous = previous, Data = JsonSerializer.Serialize(user) });
        if (changed == 1) return IdentityResult.Success;
        user.ConcurrencyStamp = previous;
        return IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "Your account changed. Please try again." });
    }
    public async Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken ct)
    {
        await using var c = await db.Open();
        await c.ExecuteAsync("DELETE FROM guru_identity_users WHERE id=@Id", new { user.Id });
        return IdentityResult.Success;
    }
    private async Task<IdentityUser?> Find(string column, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var c = await db.Open();
        var json = await c.QuerySingleOrDefaultAsync<string>($"SELECT data::text FROM guru_identity_users WHERE {column}=@value", new { value });
        return json is null ? null : JsonSerializer.Deserialize<IdentityUser>(json);
    }
    public Task<IdentityUser?> FindByIdAsync(string id, CancellationToken ct) => Find("id", id, ct);
    public Task<IdentityUser?> FindByNameAsync(string name, CancellationToken ct) => Find("normalized_username", name, ct);
    public Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken ct) => Find("normalized_email", email, ct);
    public Task<string> GetUserIdAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.Id);
    public Task<string?> GetUserNameAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.UserName);
    public Task SetUserNameAsync(IdentityUser u, string? v, CancellationToken ct) { u.UserName = v; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(IdentityUser u, string? v, CancellationToken ct) { u.NormalizedUserName = v; return Task.CompletedTask; }
    public Task SetPasswordHashAsync(IdentityUser u, string? v, CancellationToken ct) { u.PasswordHash = v; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.PasswordHash);
    public Task<bool> HasPasswordAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.PasswordHash is not null);
    public Task SetEmailAsync(IdentityUser u, string? v, CancellationToken ct) { u.Email = v; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.Email);
    public Task<bool> GetEmailConfirmedAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.EmailConfirmed);
    public Task SetEmailConfirmedAsync(IdentityUser u, bool v, CancellationToken ct) { u.EmailConfirmed = v; return Task.CompletedTask; }
    public Task<string?> GetNormalizedEmailAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.NormalizedEmail);
    public Task SetNormalizedEmailAsync(IdentityUser u, string? v, CancellationToken ct) { u.NormalizedEmail = v; return Task.CompletedTask; }
    public Task SetSecurityStampAsync(IdentityUser u, string v, CancellationToken ct) { u.SecurityStamp = v; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnd);
    public Task SetLockoutEndDateAsync(IdentityUser u, DateTimeOffset? v, CancellationToken ct) { u.LockoutEnd = v; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(++u.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(IdentityUser u, CancellationToken ct) { u.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(IdentityUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnabled);
    public Task SetLockoutEnabledAsync(IdentityUser u, bool v, CancellationToken ct) { u.LockoutEnabled = v; return Task.CompletedTask; }
}
