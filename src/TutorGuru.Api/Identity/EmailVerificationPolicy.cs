using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

namespace TutorGuru.Api.Identity;

public static class EmailVerificationPolicy
{
    // Email verification is replaced by administrator activation in every environment.
    // Activation is enforced by Identity confirmation and protected API access.
    public static bool IsRequired(IHostEnvironment environment) => false;

    public static bool CanCompleteProfile(IdentityUser? user, IHostEnvironment environment) =>
        user is not null && (user.EmailConfirmed || !IsRequired(environment));
}
