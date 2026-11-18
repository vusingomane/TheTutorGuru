using System.Text;

namespace TutorGuru.Api.Billing;

public static class BillingEndpoints
{
    public static bool ProviderPath(PathString path)
        => path == "/api/billing/paygate/notify" || path == "/api/billing/paygate/return";
    public static void MapBilling(this WebApplication app)
    {
        app.MapGet("/api/billing/config", (PayGateOptions options) => Results.Json(new
        {
            provider = "paygate",
            enabled = options.Enabled,
            mode = options.Enabled ? options.Mode : "disabled",
            message = !options.Enabled ? "PayGate checkout is not yet configured. Pending subscriptions grant no lesson access."
                : options.PublicSandbox ? "PayGate public test account: use test cards only. No real money is charged. Test subscriptions cannot unlock production lessons."
                : options.Mode == "sandbox" ? "PayGate sandbox: test transactions only. No real money is charged."
                : "Secure monthly card billing through PayGate. Full refunds within 30 days."
        }));
        app.MapGet("/api/enrollments/{enrollmentId:guid}/billing", async (Guid enrollmentId, HttpContext c, BillingService billing) =>
        {
            Tutoring.RequireRole(c, "parent");
            return Results.Json(await billing.Details(enrollmentId, Tutoring.Id(c)));
        });
        app.MapPost("/api/enrollments/{enrollmentId:guid}/checkout", async (Guid enrollmentId, CheckoutInput body, HttpContext c, BillingService billing) =>
        {
            Tutoring.RequireRole(c, "parent");
            return Results.Json(await billing.Checkout(enrollmentId, Tutoring.Id(c), body.IdempotencyKey, body.RecurringConsent));
        });
        app.MapGet("/api/enrollments/{enrollmentId:guid}/checkout", async (Guid enrollmentId, HttpContext c, BillingService billing) =>
        {
            Tutoring.RequireRole(c, "parent");
            return Results.Json(await billing.RecoverCheckout(enrollmentId, Tutoring.Id(c)));
        });
        app.MapPost("/api/enrollments/{enrollmentId:guid}/refunds", async (Guid enrollmentId, RefundInput body, HttpContext c, BillingService billing) =>
        {
            Tutoring.RequireRole(c, "parent");
            await billing.Refund(enrollmentId, Tutoring.Id(c), body.PaymentId, body.IdempotencyKey);
            return Results.Json(await billing.Details(enrollmentId, Tutoring.Id(c)));
        });
        app.MapPost("/api/enrollments/{enrollmentId:guid}/reconcile", async (Guid enrollmentId, HttpContext c, BillingService billing) =>
        {
            Tutoring.RequireRole(c, "parent");
            await billing.Reconcile(enrollmentId, Tutoring.Id(c));
            return Results.Json(await billing.Details(enrollmentId, Tutoring.Id(c)));
        });
        app.MapPost("/api/billing/paygate/notify", async (HttpContext c, BillingService billing) =>
        {
            if (c.Request.ContentType?.Split(';')[0] != "application/x-www-form-urlencoded")
                throw new HttpError(400, "PayGate notifications must be form encoded.");
            // Bound input independently of server defaults. Do not log callback contents.
            using var reader = new StreamReader(c.Request.Body, Encoding.UTF8);
            var buffer = new char[16385];
            var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (count > 16384) throw new HttpError(400, "Notification is too large.");
            await billing.Notify(PayGateClient.ParseForm(new string(buffer, 0, count)));
            return Results.Text("OK", "text/plain");
        }).DisableAntiforgery();
        app.MapPost("/api/billing/paygate/return", (HttpContext c, PayGateOptions options) =>
        {
            // Untrusted browser return cannot settle anything. The parent can query authenticated status.
            options.Require();
            c.Response.StatusCode = 303;
            c.Response.Headers.Location = options.Origin + "/subscriptions?payment=returned";
            return Task.CompletedTask;
        }).DisableAntiforgery();
    }
}
public record CheckoutInput(Guid IdempotencyKey, bool RecurringConsent);
public record RefundInput(Guid PaymentId, Guid IdempotencyKey);
