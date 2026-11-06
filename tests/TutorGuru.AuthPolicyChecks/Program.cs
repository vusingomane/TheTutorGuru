using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TutorGuru.Api.Identity;

var checks = 0;
foreach (var name in new[] { "Development", "Production", "Staging", "Unknown", "" })
{
    var environment = new TestEnvironment { EnvironmentName = name };
    var required = name != "Development";
    Check(EmailVerificationPolicy.IsRequired(environment) == required, $"{name}: sign-in policy");
    var unverified = new IdentityUser { EmailConfirmed = false };
    Check(EmailVerificationPolicy.CanCompleteProfile(unverified, environment) == !required, $"{name}: unverified profile policy");
    Check(!unverified.EmailConfirmed, $"{name}: bypass never confirms an email");
    Check(EmailVerificationPolicy.CanCompleteProfile(new IdentityUser { EmailConfirmed = true }, environment), $"{name}: verified profiles permitted");
    Check(!EmailVerificationPolicy.CanCompleteProfile(null, environment), $"{name}: missing accounts rejected");
}
Console.WriteLine($"Passed {checks} email-verification policy checks.");

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
