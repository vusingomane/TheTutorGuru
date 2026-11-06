using Microsoft.AspNetCore.Identity;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Identity;

public sealed class AccountActivation(Database db) : IUserConfirmation<IdentityUser>
{
    public async Task<bool> IsConfirmedAsync(UserManager<IdentityUser> manager, IdentityUser user)
    {
        // Existing confirmed accounts keep access. Newly registered accounts have
        // an explicit false flag, independent of email confirmation and lockout.
        var row = await db.One("""
            SELECT coalesce((data->>'PlatformActivated')::boolean,
                (data->>'EmailConfirmed')::boolean,false) AS activated
            FROM guru_identity_users WHERE id=$1
            """, user.Id);
        return row?["activated"] is true;
    }
}
