using Dapper;
using Microsoft.AspNetCore.Identity;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Identity;

public sealed class AdminProvisioner(Database db, UserManager<IdentityUser> users, ILogger<AdminProvisioner> log)
{
    public async Task Provision()
    {
        var email = Environment.GetEnvironmentVariable("ADMIN_BOOTSTRAP_EMAIL")?.Trim().ToLowerInvariant();
        var password = Environment.GetEnvironmentVariable("ADMIN_BOOTSTRAP_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email, out _) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Administrator setup requires a valid ADMIN_BOOTSTRAP_EMAIL and a secure ADMIN_BOOTSTRAP_PASSWORD.");

        await using var connection = await db.Open();
        await connection.ExecuteAsync("SELECT pg_advisory_lock(94223003)");
        try
        {
            var user = await users.FindByEmailAsync(email);
            IDictionary<string, object?>? profile = null;
            string? profileId = null;
            if (user is not null)
            {
                var link = await db.One("SELECT profile_id FROM guru_identity_profile_links WHERE identity_id=$1", user.Id);
                profileId = link?["profile_id"]?.ToString() ?? user.Id;
                profile = await db.One("SELECT id,role FROM guru_profiles WHERE id=$1", profileId);
                if (profile?["role"]?.ToString() == "admin")
                {
                    log.LogInformation("Administrator setup already completed; no account or password changes made.");
                    return;
                }
                // Never overwrite an existing password or silently take over an account.
                if (!await users.CheckPasswordAsync(user, password))
                    throw new InvalidOperationException("Administrator account already exists with a different password. No changes made.");
            }
            else
            {
                // Explicit owner-authorized provisioning, not public registration.
                user = new IdentityUser { Email = email, UserName = email, EmailConfirmed = true, LockoutEnabled = true };
                Ensure(await users.CreateAsync(user, password));
                profileId = user.Id;
            }
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                Ensure(await users.UpdateAsync(user));
            }
            await db.Query("""
                INSERT INTO guru_profiles(id,full_name,email,role) VALUES($1,'Platform Administrator',$2,'admin')
                ON CONFLICT(id) DO UPDATE SET role='admin'
                """, profileId, email);
            Ensure(await users.UpdateSecurityStampAsync(user));
            log.LogInformation("Owner-authorized administrator provisioned. No credentials were logged.");
        }
        finally { await connection.ExecuteAsync("SELECT pg_advisory_unlock(94223003)"); }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Administrator setup failed: " + string.Join(" ", result.Errors.Select(e => e.Code)));
    }
}
