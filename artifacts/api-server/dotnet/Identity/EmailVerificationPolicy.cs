using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

namespace TutorGuru.Api.Identity;

public static class EmailVerificationPolicy
{
    // Only Development may bypass verification. Production, Staging, and
    // unknown environments must continue to require a confirmed address.
    public static bool IsRequired(IHostEnvironment environment) => !environment.IsDevelopment();

    public static bool CanCompleteProfile(IdentityUser? user, IHostEnvironment environment) =>
        user is not null && (user.EmailConfirmed || !IsRequired(environment));
}
