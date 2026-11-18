using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using TutorGuru.Api.Data;

namespace TutorGuru.Api.Billing;

public sealed class BillingService(Database db, PayGateClient gateway, PayGateOptions options, IDataProtectionProvider protection)
{
    private readonly IDataProtector vaultProtection = protection.CreateProtector("TheTutorGuru.PayGate.Vault.v1");
    private const string Payments = """
        SELECT id,enrollment_id AS EnrollmentId,idempotency_key AS IdempotencyKey,kind,reference,
        amount_cents AS AmountCents,subject_ids AS SubjectIds,state,request_id AS RequestId,
        transaction_id AS TransactionId,original_payment_id AS OriginalPaymentId,
        period_start AS PeriodStart,period_end AS PeriodEnd,created_at AS CreatedAt,paid_at AS PaidAt,mode,retry_safe AS RetrySafe,merchant_id AS MerchantId
        FROM guru_billing_payments
        """;
    private const string Enrollments = """
        SELECT e.id,e.parent_id AS ParentId,e.subject_ids AS SubjectIds,e.status,e.payment_status AS PaymentStatus,
        e.period_ends_at AS PeriodEndsAt,l.grade,p.email,p.full_name AS FullName,
        b.vault_cipher AS VaultCipher,b.auto_renew AS AutoRenew,b.anchor_day AS AnchorDay,
        b.pending_subject_ids AS PendingSubjectIds,b.mode,b.merchant_id AS MerchantId
        FROM guru_enrollments e JOIN guru_learners l ON l.id=e.learner_id
        JOIN guru_profiles p ON p.id=e.parent_id LEFT JOIN guru_billing_accounts b ON b.enrollment_id=e.id
        """;
    // A session lock survives the ledger commit before the external request.
    // Every operation (worker, callback, parent) takes this same enrollment lock.
    private sealed class Lease(NpgsqlConnection connection, Guid id) : IAsyncDisposable
    {
        public NpgsqlConnection C => connection;
        public async ValueTask DisposeAsync()
        {
            try { await connection.ExecuteAsync("SELECT pg_advisory_unlock(hashtextextended(@key,79))", new { key = id.ToString() }); }
            finally { await connection.DisposeAsync(); }
        }
    }
    private async Task<Lease> Lock(Guid id)
    {
        var c = await db.Open();
        try
        {
            await c.ExecuteAsync("SELECT pg_advisory_lock(hashtextextended(@key,79))", new { key = id.ToString() });
            return new(c, id);
        }
        catch { await c.DisposeAsync(); throw; }
    }
    private static async Task<BillingEnrollment> Enrollment(NpgsqlConnection c, Guid id, string? parentId)
    {
        var e = await c.QuerySingleOrDefaultAsync<BillingEnrollment>(Enrollments + " WHERE e.id=@id", new { id })
            ?? throw new HttpError(404, "Subscription not found.");
        if (parentId != null && e.ParentId != parentId) throw new HttpError(404, "Subscription not found.");
        return e;
    }
    private static Task<BillingPayment?> Payment(NpgsqlConnection c, Guid id)
        => c.QuerySingleOrDefaultAsync<BillingPayment>(Payments + " WHERE id=@id", new { id });
    private static Task<IEnumerable<BillingPayment>> History(NpgsqlConnection c, Guid id)
        => c.QueryAsync<BillingPayment>(Payments + " WHERE enrollment_id=@id ORDER BY created_at DESC", new { id });
    private void SameMode(string? mode, string? merchantId)
    {
        if (mode != null && mode != options.Mode) throw new HttpError(409, "This subscription belongs to a different PayGate environment. Do not mix sandbox and live payments.");
        if (mode != null && merchantId != options.MerchantId) throw new HttpError(409, "This subscription belongs to a different PayGate account. Do not mix merchant accounts.");
    }
    private static void Key(Guid key)
    {
        if (key == Guid.Empty) throw new HttpError(400, "A unique payment request key is required.");
    }
    private BillingPayment NewPayment(BillingEnrollment e, Guid key, string kind, string mode, string[] subjects)
        => new() { Id = Guid.NewGuid(), EnrollmentId = e.Id, IdempotencyKey = key, Kind = kind,
            Reference = "TG-" + Guid.NewGuid().ToString("N"), AmountCents = BillingRules.Price(subjects),
            SubjectIds = subjects, State = "submitting", Mode = mode, MerchantId = options.MerchantId };
    private static Task Insert(NpgsqlConnection c, BillingPayment p, NpgsqlTransaction? tx = null)
        => c.ExecuteAsync("""
            INSERT INTO guru_billing_payments(id,enrollment_id,idempotency_key,kind,reference,amount_cents,subject_ids,state,
              original_payment_id,period_start,period_end,mode,merchant_id)
            VALUES(@Id,@EnrollmentId,@IdempotencyKey,@Kind,@Reference,@AmountCents,@SubjectIds,@State,@OriginalPaymentId,@PeriodStart,@PeriodEnd,@Mode,@MerchantId)
            """, p, tx);
    private static Task Uncertain(NpgsqlConnection c, Guid id)
        => c.ExecuteAsync("UPDATE guru_billing_payments SET state='uncertain' WHERE id=@id AND state IN ('submitting','pending')", new { id });
    private static void NoUnresolved(IEnumerable<BillingPayment> payments)
    {
        if (payments.Any(p => BillingRules.Unresolved(p.State)))
            throw new HttpError(409, "A payment is still being confirmed. Refresh payment status before making another billing change.");
    }
    public async Task<object> Details(Guid id, string parentId)
    {
        await using var c = await db.Open();
        var e = await Enrollment(c, id, parentId);
        var history = (await History(c, id)).ToList();
        var unresolved = history.Any(p => BillingRules.Unresolved(p.State));
        return new
        {
            autoRenew = e.AutoRenew,
            pendingSubjectIds = e.PendingSubjectIds ?? [],
            hasUnresolvedPayment = unresolved,
            payments = history.Select(p => new
            {
                p.Id, p.Kind, p.AmountCents, p.State, p.CreatedAt, p.PaidAt, p.PeriodStart, p.PeriodEnd,
                canRefund = !unresolved && p.Kind != "refund" && p.State == "succeeded" &&
                    BillingRules.Refundable(p.PaidAt, DateTime.UtcNow) && !history.Any(r => r.OriginalPaymentId == p.Id && !r.RetrySafe)
            })
        };
    }
    public async Task<BillingCheckout> Checkout(Guid id, string parentId, Guid key, bool consent)
    {
        Key(key);
        if (!consent) throw new HttpError(400, "Consent to monthly card billing is required.");
        options.Require();
        await using var lease = await Lock(id);
        var c = lease.C;
        var e = await Enrollment(c, id, parentId);
        SameMode(e.Mode, e.MerchantId);
        var existing = await c.QuerySingleOrDefaultAsync<BillingPayment>(Payments + " WHERE idempotency_key=@key", new { key });
        if (existing != null)
        {
            if (existing.EnrollmentId != id || existing.Kind != "checkout") throw new HttpError(409, "This payment key is already in use.");
            SameMode(existing.Mode, existing.MerchantId);
            if (existing.State != "pending") throw new HttpError(409, "This payment request has already been processed. Refresh payment status.");
            return gateway.CheckoutForm(existing);
        }
        NoUnresolved(await History(c, id));
        if (e.Status == "cancelled") throw new HttpError(409, "Create a new subscription instead of paying a cancelled subscription.");
        if (e.PaymentStatus == "paid" && e.PeriodEndsAt > DateTime.UtcNow)
            throw new HttpError(409, "This subscription is already paid. Changes apply at the next renewal.");
        var p = NewPayment(e, key, "checkout", options.Mode, e.PendingSubjectIds ?? e.SubjectIds);
        // Commit the intent before sending any request; an interrupted request stays unresolved.
        await using (var tx = await c.BeginTransactionAsync())
        {
            await c.ExecuteAsync("""
                INSERT INTO guru_billing_accounts(enrollment_id,consent_at,mode,merchant_id) VALUES(@id,now(),@mode,@merchant)
                ON CONFLICT(enrollment_id) DO UPDATE SET consent_at=now()
                """, new { id, mode = options.Mode, merchant = options.MerchantId }, tx);
            await Insert(c, p, tx);
            await tx.CommitAsync();
        }
        try
        {
            p.RequestId = await gateway.Initiate(p, e.Email);
            await c.ExecuteAsync("UPDATE guru_billing_payments SET request_id=@RequestId,state='pending' WHERE id=@Id", p);
            return gateway.CheckoutForm(p);
        }
        catch
        {
            await Uncertain(c, p.Id);
            throw new HttpError(503, "PayGate did not confirm checkout creation. Refresh payment status; no second request will be sent automatically.");
        }
    }
    public async Task<BillingCheckout> RecoverCheckout(Guid id, string parentId)
    {
        options.Require();
        await using var lease = await Lock(id);
        var e = await Enrollment(lease.C, id, parentId);
        SameMode(e.Mode, e.MerchantId);
        var p = (await History(lease.C, id)).FirstOrDefault(p => p.Kind == "checkout" && p.State == "pending" && p.RequestId != null)
            ?? throw new HttpError(404, "No pending hosted checkout is available. Refresh payment status.");
        SameMode(p.Mode, p.MerchantId);
        return gateway.CheckoutForm(p);
    }
    public async Task Change(Guid id, string parentId, EnrollmentUpdate body, Tutoring domain)
    {
        await using var lease = await Lock(id);
        var c = lease.C;
        var e = await Enrollment(c, id, parentId);
        NoUnresolved(await History(c, id));
        if (e.Status == "cancelled") throw new HttpError(409, "This subscription is cancelled. Create a new one to subscribe again.");
        if (body.Action == "change") await domain.ValidateSubjects(body.SubjectIds, e.Grade);
        var paid = e.PaymentStatus == "paid" && e.PeriodEndsAt > DateTime.UtcNow;
        if (paid && e.Mode == null) throw new HttpError(409, "This subscription is not managed by this PayGate integration. No billing change has been made.");
        await using var tx = await c.BeginTransactionAsync();
        switch (body.Action)
        {
            case "change":
                if (paid)
                    await c.ExecuteAsync("UPDATE guru_billing_accounts SET pending_subject_ids=@subjects WHERE enrollment_id=@id", new { subjects = body.SubjectIds, id }, tx);
                else
                {
                    await c.ExecuteAsync("UPDATE guru_enrollments SET subject_ids=@subjects,monthly_amount_zar=@price WHERE id=@id",
                        new { subjects = body.SubjectIds, price = BillingRules.Price(body.SubjectIds!) / 100, id }, tx);
                    await c.ExecuteAsync("UPDATE guru_billing_accounts SET pending_subject_ids=NULL WHERE enrollment_id=@id", new { id }, tx);
                }
                break;
            case "cancel":
                await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=false WHERE enrollment_id=@id", new { id }, tx);
                await c.ExecuteAsync("UPDATE guru_enrollments SET status=@status WHERE id=@id", new { status = paid ? "cancelling" : "cancelled", id }, tx);
                break;
            case "resume":
                if (!paid || e.Status != "cancelling" || e.VaultCipher == null) throw new HttpError(409, "Only a paid subscription scheduled to cancel can be resumed.");
                options.Require(); SameMode(e.Mode, e.MerchantId);
                await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=true WHERE enrollment_id=@id", new { id }, tx);
                await c.ExecuteAsync("UPDATE guru_enrollments SET status='active' WHERE id=@id", new { id }, tx);
                break;
            default: throw new HttpError(400, "Choose cancel, resume, or change.");
        }
        await tx.CommitAsync();
    }
    public async Task Refund(Guid id, string parentId, Guid paymentId, Guid key)
    {
        Key(key); options.Require();
        await using var lease = await Lock(id);
        var c = lease.C;
        var e = await Enrollment(c, id, parentId);
        SameMode(e.Mode, e.MerchantId);
        var original = await Payment(c, paymentId);
        if (original == null || original.EnrollmentId != id || original.Kind == "refund") throw new HttpError(404, "Payment not found.");
        var existing = await c.QuerySingleOrDefaultAsync<BillingPayment>(Payments + " WHERE idempotency_key=@key", new { key });
        if (existing == null) existing = await c.QuerySingleOrDefaultAsync<BillingPayment>(Payments + " WHERE original_payment_id=@paymentId AND NOT retry_safe", new { paymentId });
        if (existing != null)
        {
            if (existing.EnrollmentId != id || existing.Kind != "refund" || existing.OriginalPaymentId != paymentId)
                throw new HttpError(409, "This payment key is already in use.");
            return; // Never send a second refund, including when the first response was lost.
        }
        NoUnresolved(await History(c, id));
        if (original.State != "succeeded" || !BillingRules.Refundable(original.PaidAt, DateTime.UtcNow))
            throw new HttpError(409, "Full refunds are available within 30 days of confirmed payment.");
        SameMode(original.Mode, original.MerchantId);
        var p = NewPayment(e, key, "refund", options.Mode, original.SubjectIds);
        p.OriginalPaymentId = original.Id;
        await using (var tx = await c.BeginTransactionAsync())
        {
            await Insert(c, p, tx);
            // Stop the scheduler before the refund, including if the gateway times out.
            await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=false WHERE enrollment_id=@id", new { id }, tx);
            if (e.Status != "cancelled") await c.ExecuteAsync("UPDATE guru_enrollments SET status='cancelling' WHERE id=@id", new { id }, tx);
            await tx.CommitAsync();
        }
        try { await Apply(c, e, p, await gateway.Refund(original), original, submission: true); }
        catch
        {
            await Uncertain(c, p.Id);
            throw new HttpError(503, "Refund confirmation is pending. Renewals are stopped. Refresh payment status; do not submit another refund.");
        }
    }
    public async Task Notify(Dictionary<string, string> fields)
    {
        gateway.Authenticate(fields);
        var reference = fields.GetValueOrDefault("REFERENCE") ?? "";
        var row = await db.One("SELECT enrollment_id FROM guru_billing_payments WHERE reference=$1 AND kind='checkout'", reference)
            ?? throw new HttpError(400, "Unknown payment reference.");
        await using var lease = await Lock((Guid)row["enrollment_id"]!);
        var c = lease.C;
        var p = await c.QuerySingleAsync<BillingPayment>(Payments + " WHERE reference=@reference", new { reference });
        SameMode(p.Mode, p.MerchantId);
        if (!Guid.TryParse(fields.GetValueOrDefault("PAY_REQUEST_ID"), out var requestId) ||
            p.RequestId != null && !string.Equals(p.RequestId, requestId.ToString(), StringComparison.OrdinalIgnoreCase) ||
            fields.GetValueOrDefault("AMOUNT") != p.AmountCents.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            fields.GetValueOrDefault("CURRENCY") != "ZAR")
            throw new HttpError(400, "PayGate payment details do not match.");
        if (p.State == "succeeded") return;
        // Server-side query independently confirms callbacks, including delayed/out-of-order delivery.
        p.RequestId ??= fields["PAY_REQUEST_ID"];
        await c.ExecuteAsync("UPDATE guru_billing_payments SET request_id=@RequestId WHERE id=@Id AND request_id IS NULL", p);
        var e = await Enrollment(c, p.EnrollmentId, null);
        await Apply(c, e, p, await gateway.QueryWeb(p));
    }
    public async Task Reconcile(Guid id, string? parentId)
    {
        options.Require();
        await using var lease = await Lock(id);
        var c = lease.C;
        var e = await Enrollment(c, id, parentId);
        SameMode(e.Mode, e.MerchantId);
        foreach (var p in (await History(c, id)).Where(p => BillingRules.Unresolved(p.State) ||
            p.Kind == "refund" && p.State == "failed" && !p.RetrySafe))
        {
            SameMode(p.Mode, p.MerchantId);
            var original = p.OriginalPaymentId.HasValue ? await Payment(c, p.OriginalPaymentId.Value) : null;
            var result = p.Kind == "checkout" && p.RequestId != null
                ? await gateway.QueryWeb(p) : await gateway.QueryHost(p, original);
            await Apply(c, e, p, result, original);
        }
    }
    private async Task Apply(NpgsqlConnection c, BillingEnrollment e, BillingPayment p, PayGateResult r, BillingPayment? original = null, bool submission = false)
    {
        if (p.State == "succeeded") return; // Terminal success can never be downgraded by an older event.
        if (r.RejectedBeforeProcessing)
        {
            // Only a definitive rejection of the ORIGINAL submission proves that no operation was
            // processed. Authentication failure of a later query says nothing about its outcome.
            await using var rejected = await c.BeginTransactionAsync();
            await c.ExecuteAsync("""
                UPDATE guru_billing_payments SET state=@state,result_code=@code,retry_safe=@safe WHERE id=@id
                """, new { state = submission ? "failed" : "uncertain", code = r.ResultCode, safe = submission && p.Kind == "refund", id = p.Id }, rejected);
            if (submission && p.Kind != "refund")
            {
                await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=false WHERE enrollment_id=@id", new { id = e.Id }, rejected);
                await c.ExecuteAsync("UPDATE guru_enrollments SET status='past_due',payment_status='failed' WHERE id=@id", new { id = e.Id }, rejected);
            }
            await rejected.CommitAsync();
            return;
        }
        if (r.Reference != null && r.Reference != (original?.Reference ?? p.Reference) ||
            r.Currency != null && r.Currency != "ZAR" || r.AmountCents.HasValue && r.AmountCents != p.AmountCents ||
            p.RequestId != null && r.RequestId != null && !string.Equals(p.RequestId, r.RequestId, StringComparison.OrdinalIgnoreCase))
            throw new HttpError(503, "Provider response did not match the payment. No billing change has been made.");
        // PayHost follow-up codes differ from PayWeb authorisation: status 4 + 990012 is
        // "Refund done", not a card decline or user cancellation.
        var confirmed = p.Kind == "refund" ? r.Status == "4" && r.ResultCode == "990012"
            : r.Status == "1" && r.ResultCode == "990017";
        if (confirmed)
        {
            if (r.AmountCents != p.AmountCents || r.Currency != "ZAR" || r.Reference != (original?.Reference ?? p.Reference) ||
                string.IsNullOrWhiteSpace(r.TransactionId) || p.Kind != "refund" && r.ResultCode != "990017" ||
                p.RequestId != null && !string.Equals(r.RequestId, p.RequestId, StringComparison.OrdinalIgnoreCase))
                throw new HttpError(503, "Provider confirmation did not match the payment. No access has been granted.");
            var now = DateTime.UtcNow;
            var start = p.Kind == "renewal" ? p.PeriodStart!.Value : now;
            var anchor = p.Kind == "renewal" ? e.AnchorDay!.Value : start.Day;
            var end = BillingRules.NextPeriod(start, anchor);
            await using var tx = await c.BeginTransactionAsync();
            await c.ExecuteAsync("""
                UPDATE guru_billing_payments SET state='succeeded',transaction_id=@transaction,
                 paid_at=now(),result_code=@code,period_start=@start,period_end=@end,retry_safe=false WHERE id=@id
                """, new { transaction = r.TransactionId, code = r.ResultCode, start = p.Kind == "refund" ? (DateTime?)null : start,
                    end = p.Kind == "refund" ? (DateTime?)null : end, id = p.Id }, tx);
            if (p.Kind == "refund")
            {
                await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=false,pending_subject_ids=NULL WHERE enrollment_id=@id", new { id = e.Id }, tx);
                await c.ExecuteAsync("UPDATE guru_enrollments SET status='cancelled',payment_status='refunded',period_ends_at=now() WHERE id=@id", new { id = e.Id }, tx);
            }
            else
            {
                // Renewals retain the original token; checkout consent produces a new encrypted token.
                var vault = p.Kind == "checkout" ? r.VaultId : null;
                var cipher = p.Kind == "checkout"
                    ? (!string.IsNullOrWhiteSpace(vault) ? vaultProtection.Protect(vault) : null)
                    : e.VaultCipher;
                var autoRenew = p.Kind == "checkout" ? cipher != null : e.AutoRenew;
                await c.ExecuteAsync("""
                    UPDATE guru_billing_accounts SET vault_cipher=@cipher,auto_renew=@autoRenew,anchor_day=@anchor,
                    pending_subject_ids=NULL WHERE enrollment_id=@id
                    """, new { cipher, autoRenew, anchor, id = e.Id }, tx);
                await c.ExecuteAsync("""
                    UPDATE guru_enrollments SET status=@status,payment_status='paid',period_ends_at=@end,
                    subject_ids=@subjects,monthly_amount_zar=@price WHERE id=@id
                    """, new { status = autoRenew ? "active" : "cancelling", end, subjects = p.SubjectIds, price = p.AmountCents / 100, id = e.Id }, tx);
            }
            await tx.CommitAsync();
        }
        else if (r.Status is "2" or "3" or "4")
        {
            // A provider-confirmed decline/cancellation permits a new hosted checkout. No automatic retry.
            await using var tx = await c.BeginTransactionAsync();
            await c.ExecuteAsync("UPDATE guru_billing_payments SET state='failed',result_code=@code WHERE id=@id AND state<>'succeeded'", new { code = r.ResultCode, id = p.Id }, tx);
            if (p.Kind != "refund" && !(e.PaymentStatus == "paid" && e.PeriodEndsAt > DateTime.UtcNow))
            {
                await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=false WHERE enrollment_id=@id", new { id = e.Id }, tx);
                await c.ExecuteAsync("UPDATE guru_enrollments SET status='past_due',payment_status='failed' WHERE id=@id", new { id = e.Id }, tx);
            }
            await tx.CommitAsync();
        }
        else await c.ExecuteAsync("UPDATE guru_billing_payments SET state='pending',result_code=@code WHERE id=@id AND state<>'succeeded'", new { code = r.ResultCode, id = p.Id });
    }
    public async Task RenewDue(Guid id)
    {
        options.Require();
        await using var lease = await Lock(id);
        var c = lease.C;
        var e = await Enrollment(c, id, null);
        SameMode(e.Mode, e.MerchantId);
        if (e.PeriodEndsAt == null || e.PeriodEndsAt > DateTime.UtcNow || e.Status is not ("active" or "cancelling")) return;
        // Never renew a period while another operation has an unknown outcome.
        if ((await History(c, id)).Any(p => BillingRules.Unresolved(p.State))) return;
        if (!e.AutoRenew)
        {
            await c.ExecuteAsync("UPDATE guru_enrollments SET status='cancelled' WHERE id=@id", new { id });
            return;
        }
        if (e.VaultCipher == null || e.AnchorDay == null) throw new HttpError(409, "A verified card mandate is required for renewal.");
        if (await c.QuerySingleOrDefaultAsync<BillingPayment>(Payments + " WHERE enrollment_id=@id AND kind='renewal' AND period_start=@start",
            new { id, start = e.PeriodEndsAt }) != null) return;
        // After a long outage do not collect multiple old months. Require a fresh customer checkout.
        if (BillingRules.NextPeriod(e.PeriodEndsAt.Value, e.AnchorDay.Value) <= DateTime.UtcNow)
        {
            await using var tx = await c.BeginTransactionAsync();
            await c.ExecuteAsync("UPDATE guru_billing_accounts SET auto_renew=false WHERE enrollment_id=@id", new { id }, tx);
            await c.ExecuteAsync("UPDATE guru_enrollments SET status='past_due',payment_status='failed' WHERE id=@id", new { id }, tx);
            await tx.CommitAsync();
            return;
        }
        var p = NewPayment(e, Guid.NewGuid(), "renewal", options.Mode, e.PendingSubjectIds ?? e.SubjectIds);
        p.PeriodStart = e.PeriodEndsAt;
        p.PeriodEnd = BillingRules.NextPeriod(p.PeriodStart.Value, e.AnchorDay.Value);
        var vault = vaultProtection.Unprotect(e.VaultCipher);
        await Insert(c, p); // Durable before sending. Never resubmit this period, even after a crash.
        try { await Apply(c, e, p, await gateway.Renew(p, e, vault), submission: true); }
        catch { await Uncertain(c, p.Id); throw; }
    }
}
