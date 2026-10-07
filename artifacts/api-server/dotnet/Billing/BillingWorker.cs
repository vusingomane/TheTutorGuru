using TutorGuru.Api.Data;

namespace TutorGuru.Api.Billing;

// The durable ledger and enrollment locks make this safe across multiple API instances.
public sealed class BillingWorker(IServiceScopeFactory scopes, PayGateOptions options, ILogger<BillingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.Enabled)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Database>();
                    var billing = scope.ServiceProvider.GetRequiredService<BillingService>();
                    // Reconcile unknown outcomes before considering any new renewal.
                    var unresolved = await db.Query("""
                        SELECT DISTINCT enrollment_id FROM guru_billing_payments
                        WHERE state IN ('submitting','pending','uncertain') AND mode=$1 AND merchant_id=$2
                        AND created_at<now()-interval '2 minutes' LIMIT 50
                        """, options.Mode, options.MerchantId);
                    foreach (var row in unresolved)
                    {
                        if (stoppingToken.IsCancellationRequested) return;
                        try { await billing.Reconcile((Guid)row["enrollment_id"]!, null); }
                        catch (Exception e) { log.LogWarning("PayGate reconciliation requires attention ({Type}). No charge was retried.", e.GetType().Name); }
                    }
                    var due = await db.Query("""
                        SELECT e.id FROM guru_enrollments e JOIN guru_billing_accounts b ON b.enrollment_id=e.id
                        WHERE e.status IN ('active','cancelling') AND e.period_ends_at<=now() AND b.mode=$1 AND b.merchant_id=$2
                        ORDER BY e.period_ends_at LIMIT 50
                        """, options.Mode, options.MerchantId);
                    foreach (var row in due)
                    {
                        if (stoppingToken.IsCancellationRequested) return;
                        try { await billing.RenewDue((Guid)row["id"]!); }
                        catch (Exception e) { log.LogWarning("PayGate renewal requires attention ({Type}). Check the payment ledger.", e.GetType().Name); }
                    }
                }
                catch (Exception e) { log.LogWarning("Billing worker could not run ({Type}). No blind retry will be attempted.", e.GetType().Name); }
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
