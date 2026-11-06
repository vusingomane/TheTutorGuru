using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TutorGuru.Api.Identity;

var checks = 0;
foreach (var name in new[] { "Development", "Production", "Staging", "Unknown", "" })
{
    var environment = new TestEnvironment { EnvironmentName = name };
    var required = false;
    Check(EmailVerificationPolicy.IsRequired(environment) == required, $"{name}: sign-in policy");
    var unverified = new IdentityUser { EmailConfirmed = false };
    Check(EmailVerificationPolicy.CanCompleteProfile(unverified, environment) == !required, $"{name}: unverified profile policy");
    Check(!unverified.EmailConfirmed, $"{name}: bypass never confirms an email");
    Check(EmailVerificationPolicy.CanCompleteProfile(new IdentityUser { EmailConfirmed = true }, environment), $"{name}: verified profiles permitted");
    Check(!EmailVerificationPolicy.CanCompleteProfile(null, environment), $"{name}: missing accounts rejected");
}
AdminSetupException.Ensure(IdentityResult.Success, "IdentityAccountCreationFailed");
Check(true, "successful Identity result does not fail");
try
{
    AdminSetupException.Ensure(IdentityResult.Failed(
        new IdentityError { Code = "PasswordTooShort", Description = "synthetic-private-description" },
        new IdentityError { Code = "synthetic-private-code", Description = "synthetic-private-description" },
        new IdentityError { Code = "PasswordTooShort", Description = "synthetic-private-description" }),
        "IdentityAccountCreationFailed");
    throw new InvalidOperationException("Expected setup failure.");
}
catch (AdminSetupException error)
{
    Check(error.Category == "IdentityAccountCreationFailed", "failure preserves stage category");
    Check(error.IdentityCodes == "OtherIdentityError,PasswordTooShort", "only allowlisted codes logged, deduplicated");
    Check(!error.ToString().Contains("synthetic-private", StringComparison.Ordinal), "exception excludes descriptions and unknown codes");
    Check(error.InnerException is null, "no unsafe inner exception");
}
Console.WriteLine($"Passed {checks} authentication policy and safe diagnostic checks.");

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}

sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "";
    public string ApplicationName { get; set; } = "AuthPolicyChecks";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
