using Microsoft.AspNetCore.Identity;

namespace TutorGuru.Api.Identity;

// Only fixed categories and known Identity codes may reach operational logs.
// Never include Identity descriptions, input values, or inner exception messages.
public sealed class AdminSetupException(string category, string identityCodes = "none") : Exception("Administrator setup did not complete.")
{
    public string Category { get; } = category;
    public string IdentityCodes { get; } = identityCodes;

    private static readonly HashSet<string> SafeIdentityCodes = new(StringComparer.Ordinal)
    {
        "DefaultError", "ConcurrencyFailure", "PasswordMismatch", "InvalidToken",
        "LoginAlreadyAssociated", "InvalidUserName", "InvalidEmail", "DuplicateUserName",
        "DuplicateEmail", "InvalidRoleName", "DuplicateRoleName", "UserAlreadyHasPassword",
        "UserLockoutNotEnabled", "UserAlreadyInRole", "UserNotInRole", "PasswordTooShort",
        "PasswordRequiresUniqueChars", "PasswordRequiresNonAlphanumeric",
        "PasswordRequiresDigit", "PasswordRequiresLower", "PasswordRequiresUpper"
    };

    public static void Ensure(IdentityResult result, string category)
    {
        if (!result.Succeeded)
        {
            var codes = result.Errors.Select(e => SafeIdentityCodes.Contains(e.Code) ? e.Code : "OtherIdentityError")
                .Distinct().OrderBy(code => code, StringComparer.Ordinal);
            throw new AdminSetupException(category, string.Join(",", codes));
        }
    }
}
