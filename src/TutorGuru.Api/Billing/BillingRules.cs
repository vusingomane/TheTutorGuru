namespace TutorGuru.Api.Billing;

public static class BillingRules
{
    public static int Price(string[] subjects)
    {
        if (subjects.Length is < 1 or > 2 || subjects.Distinct().Count() != subjects.Length)
            throw new HttpError(400, "Choose one or two different subjects.");
        return subjects.Length == 1 ? 29000 : 49500;
    }
    // Preserve the original billing day through short months (Jan 31 -> Feb 28 -> Mar 31).
    public static DateTime NextPeriod(DateTime start, int anchorDay)
    {
        var next = start.AddMonths(1);
        return new DateTime(next.Year, next.Month, Math.Min(anchorDay, DateTime.DaysInMonth(next.Year, next.Month)),
            start.Hour, start.Minute, start.Second, DateTimeKind.Utc);
    }
    public static bool Refundable(DateTime? paidAt, DateTime now)
        => paidAt.HasValue && now >= paidAt.Value && now <= paidAt.Value.AddDays(30);
    public static bool Unresolved(string state) => state is "submitting" or "pending" or "uncertain";
}

public sealed class BillingPayment
{
    public Guid Id { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public int AmountCents { get; set; }
    public string[] SubjectIds { get; set; } = [];
    public string State { get; set; } = "";
    public string? RequestId { get; set; }
    public string? TransactionId { get; set; }
    public Guid? OriginalPaymentId { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string Mode { get; set; } = "";
    public string? MerchantId { get; set; }
    public bool RetrySafe { get; set; }
}

public sealed class BillingEnrollment
{
    public Guid Id { get; set; }
    public string ParentId { get; set; } = "";
    public string[] SubjectIds { get; set; } = [];
    public string Status { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
    public DateTime? PeriodEndsAt { get; set; }
    public int Grade { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? VaultCipher { get; set; }
    public bool AutoRenew { get; set; }
    public int? AnchorDay { get; set; }
    public string[]? PendingSubjectIds { get; set; }
    public string? Mode { get; set; }
    public string? MerchantId { get; set; }
}
