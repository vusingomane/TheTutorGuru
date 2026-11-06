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
        if (string.IsNullOrWhiteSpace(email))
            throw new AdminSetupException("MissingEmailConfiguration");
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _))
            throw new AdminSetupException("InvalidEmailConfiguration");
        if (string.IsNullOrEmpty(password))
            throw new AdminSetupException("MissingPasswordConfiguration");

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
                    throw new AdminSetupException("ExistingAccountPasswordMismatch");
            }
            else
            {
                // Explicit owner-authorized provisioning, not public registration.
                user = new IdentityUser { Email = email, UserName = email, EmailConfirmed = true, LockoutEnabled = true };
                AdminSetupException.Ensure(await users.CreateAsync(user, password), "IdentityAccountCreationFailed");
                profileId = user.Id;
            }
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                AdminSetupException.Ensure(await users.UpdateAsync(user), "IdentityConfirmationFailed");
            }
            await db.Query("""
                INSERT INTO guru_profiles(id,full_name,email,role) VALUES($1,'Platform Administrator',$2,'admin')
                ON CONFLICT(id) DO UPDATE SET role='admin'
                """, profileId, email);
            AdminSetupException.Ensure(await users.UpdateSecurityStampAsync(user), "IdentitySecurityStampUpdateFailed");
            log.LogInformation("Owner-authorized administrator provisioned. No credentials were logged.");
        }
        finally { await connection.ExecuteAsync("SELECT pg_advisory_unlock(94223003)"); }
    }

}
