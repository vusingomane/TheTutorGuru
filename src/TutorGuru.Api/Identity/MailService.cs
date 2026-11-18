using System.Net;
using System.Net.Mail;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Identity;

public sealed class MailService(Database db, IWebHostEnvironment env)
{
    public bool Available => env.IsDevelopment() || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_HOST"));
    public async Task Send(string email, string subject, string link)
    {
        if (env.IsDevelopment())
        {
            // Private operator mailbox, never exposed over HTTP or printed in logs.
            await db.Query("INSERT INTO guru_identity_dev_mail(recipient,subject,link) VALUES($1,$2,$3)", email, subject, link);
            return;
        }
        var host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? throw new HttpError(503, "Account email delivery is not configured.");
        using var client = new SmtpClient(host, int.Parse(Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587"))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(Environment.GetEnvironmentVariable("SMTP_USERNAME"), Environment.GetEnvironmentVariable("SMTP_PASSWORD"))
        };
        using var message = new MailMessage(Environment.GetEnvironmentVariable("SMTP_FROM") ?? throw new HttpError(503, "Account email sender is not configured."), email, subject,
            $"TheTutorGuru\n\n{link}\n\nIf you did not request this, ignore this email.");
        await client.SendMailAsync(message);
    }
}
