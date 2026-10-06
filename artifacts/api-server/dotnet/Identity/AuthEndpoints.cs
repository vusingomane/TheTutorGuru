using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Identity;

public static class AuthEndpoints
{
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { throw new HttpError(400, "This account link is invalid."); }
    }
    private static string Origin(HttpContext c, IWebHostEnvironment env)
    {
        var configured = Environment.GetEnvironmentVariable("APP_ORIGIN");
        if (!env.IsDevelopment() && string.IsNullOrEmpty(configured))
            throw new HttpError(503, "APP_ORIGIN must be configured before account email delivery.");
        var origin = configured ?? $"{c.Request.Scheme}://{c.Request.Host}";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (!env.IsDevelopment() && uri.Scheme != "https") || uri.PathAndQuery != "/" || uri.Fragment != "" || uri.UserInfo != "")
            throw new HttpError(503, "APP_ORIGIN must be the HTTPS application origin, without a path.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public static void MapAuth(this WebApplication app)
    {
        var routes = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        routes.MapGet("/csrf", (HttpContext c, IAntiforgery csrf) => Results.Json(new { token = csrf.GetAndStoreTokens(c).RequestToken })).DisableRateLimiting();
        routes.MapGet("/me", async (HttpContext c, UserManager<IdentityUser> users) =>
        {
            var u = await users.GetUserAsync(c.User);
            return u is null ? Results.Json(new { error = "Please sign in." }, statusCode: 401) : Results.Json(new { email = u.Email, emailConfirmed = u.EmailConfirmed });
        }).DisableRateLimiting();
        routes.MapPost("/register", async (AuthInput body, UserManager<IdentityUser> users, MailService mail, HttpContext c, IWebHostEnvironment env) =>
        {
            body.Validate();
            if (!mail.Available) throw new HttpError(503, "Account email delivery must be configured before registration.");
            var origin = Origin(c, env);
            var user = new IdentityUser { UserName = body.Email.Trim().ToLowerInvariant(), Email = body.Email.Trim().ToLowerInvariant(), LockoutEnabled = true };
            var result = await users.CreateAsync(user, body.Password);
            if (!result.Succeeded) throw new HttpError(400, string.Join(" ", result.Errors.Select(e => e.Description)));
            await mail.Send(user.Email, "Verify your TheTutorGuru email", $"{origin}/verify-email?userId={Uri.EscapeDataString(user.Id)}&token={Encode(await users.GenerateEmailConfirmationTokenAsync(user))}");
            return Results.Json(new { message = env.IsDevelopment() ? "Account created. Ask the workspace operator for the development verification email." : "Account created. Check your email to verify your address." }, statusCode: 201);
        });
        routes.MapPost("/login", async (AuthInput body, SignInManager<IdentityUser> signIn) =>
        {
            body.Validate();
            var result = await signIn.PasswordSignInAsync(body.Email.Trim().ToLowerInvariant(), body.Password, true, true);
            if (!result.Succeeded) throw new HttpError(result.IsLockedOut ? 429 : 401, result.IsNotAllowed ? "Verify your email before signing in." : "Sign-in failed. Check your details or try again later.");
            return Results.Json(new { message = "Signed in." });
        });
        routes.MapPost("/logout", async (SignInManager<IdentityUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); });
        routes.MapPost("/confirm-email", async (TokenInput body, UserManager<IdentityUser> users) =>
        {
            body.Validate();
            var u = await users.FindByIdAsync(body.UserId);
            if (u is null || u.EmailConfirmed || !(await users.ConfirmEmailAsync(u, Decode(body.Token))).Succeeded)
                throw new HttpError(400, "The verification link is invalid, expired, or already used.");
            return Results.Json(new { message = "Email verified. You can now sign in." });
        });
        routes.MapPost("/resend-confirmation", async (EmailInput body, UserManager<IdentityUser> users, MailService mail, HttpContext c, IWebHostEnvironment env) =>
        {
            body.Validate();
            if (!mail.Available) throw new HttpError(503, "Account email delivery is not configured.");
            var origin = Origin(c, env);
            var u = await users.FindByEmailAsync(body.Email.Trim().ToLowerInvariant());
            if (u is not null && !u.EmailConfirmed)
                await mail.Send(u.Email!, "Verify your TheTutorGuru email", $"{origin}/verify-email?userId={Uri.EscapeDataString(u.Id)}&token={Encode(await users.GenerateEmailConfirmationTokenAsync(u))}");
            return Results.Json(new { message = "If this account needs verification, a new link has been sent. Development mail is held privately for the workspace operator." });
        });
        routes.MapPost("/forgot-password", async (EmailInput body, UserManager<IdentityUser> users, MailService mail, HttpContext c, IWebHostEnvironment env) =>
        {
            body.Validate();
            if (!mail.Available) throw new HttpError(503, "Account email delivery is not configured.");
            var origin = Origin(c, env);
            var u = await users.FindByEmailAsync(body.Email.Trim().ToLowerInvariant());
            if (u is not null && u.EmailConfirmed)
                await mail.Send(u.Email!, "Reset your TheTutorGuru password", $"{origin}/reset-password?userId={Uri.EscapeDataString(u.Id)}&token={Encode(await users.GeneratePasswordResetTokenAsync(u))}");
            return Results.Json(new { message = "If a verified account exists, a password reset email has been sent. Development mail is held privately for the workspace operator." });
        });
        routes.MapPost("/reset-password", async (ResetInput body, UserManager<IdentityUser> users) =>
        {
            body.Validate();
            var u = await users.FindByIdAsync(body.UserId);
            if (u is null) throw new HttpError(400, "This reset link is invalid.");
            var result = await users.ResetPasswordAsync(u, Decode(body.Token), body.Password);
            if (!result.Succeeded) throw new HttpError(400, string.Join(" ", result.Errors.Select(e => e.Description)));
            return Results.Json(new { message = "Password reset. Sign in with your new password." });
        });
        routes.MapPost("/migrate", async (MigrationInput body, HttpContext ctx, UserManager<IdentityUser> users, Database db) =>
        {
            var user = await users.GetUserAsync(ctx.User);
            if (user is null || !user.EmailConfirmed) throw new HttpError(401, "Sign in with a verified account first.");
            if (string.IsNullOrWhiteSpace(body.Code)) throw new HttpError(400, "Enter your migration invitation.");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.Code.Trim())));
            await using var c = await db.Open();
            await using var tx = await c.BeginTransactionAsync();
            var invite = await c.QuerySingleOrDefaultAsync<Invitation>("SELECT profile_id AS ProfileId,email AS Email FROM guru_identity_invitations WHERE code_hash=@hash AND used_at IS NULL AND expires_at>now() FOR UPDATE", new { hash }, tx);
            if (invite is null || !string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase)) throw new HttpError(400, "This invitation is invalid, expired, used, or belongs to another email.");
            if (await c.ExecuteScalarAsync<int>("SELECT count(*) FROM guru_identity_profile_links WHERE identity_id=@Id OR profile_id=@ProfileId", new { user.Id, invite.ProfileId }, tx) != 0)
                throw new HttpError(409, "This identity or existing profile is already linked.");
            // Never rewrite any legacy profile IDs or ownership references.
            await c.ExecuteAsync("INSERT INTO guru_identity_profile_links(identity_id,profile_id) VALUES(@Id,@ProfileId)", new { user.Id, invite.ProfileId }, tx);
            await c.ExecuteAsync("UPDATE guru_identity_invitations SET used_at=now() WHERE code_hash=@hash", new { hash }, tx);
            await tx.CommitAsync();
            return Results.Json(new { message = "Your existing tutoring account is linked. Your role, learners, courses and files are unchanged." });
        });
    }
    private sealed record Invitation(string ProfileId, string Email);
}
public record EmailInput(string Email)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 320 || !System.Net.Mail.MailAddress.TryCreate(Email, out _))
            throw new HttpError(400, "Enter a valid email address.");
    }
}
public record AuthInput(string Email, string Password)
{
    public void Validate()
    {
        new EmailInput(Email).Validate();
        if (string.IsNullOrEmpty(Password) || Password.Length > 1024)
            throw new HttpError(400, "Enter a valid email address and password.");
    }
}
public record TokenInput(string UserId, string Token)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UserId) || UserId.Length > 128 || string.IsNullOrWhiteSpace(Token) || Token.Length > 4096)
            throw new HttpError(400, "This account link is invalid.");
    }
}
public record ResetInput(string UserId, string Token, string Password)
{
    public void Validate()
    {
        new TokenInput(UserId, Token).Validate();
        if (string.IsNullOrEmpty(Password) || Password.Length > 1024) throw new HttpError(400, "Enter your new password.");
    }
}
public record MigrationInput(string Code);
