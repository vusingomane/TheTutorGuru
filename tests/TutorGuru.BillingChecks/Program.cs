using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TutorGuru.Api;
using TutorGuru.Api.Billing;
using TutorGuru.Api.Data;
using TutorGuru.Api.Identity;

// No real provider calls. Billing state checks use a private disposable PostgreSQL schema,
// never existing learner/payment rows. HTTP fixtures validate the actual provider wire contract.
var checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("FAILED: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
async Task Reject(Func<Task> action, int status, string label)
{
    try { await action(); throw new Exception("Expected rejection: " + label); }
    catch (HttpError e) { Check(e.Status == status, label); }
}
var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
Check(BillingRules.Price(["math"]) == 29000 && BillingRules.Price(["math", "science"]) == 49500, "Exact ZAR prices");
await Reject(() => Task.FromResult(BillingRules.Price(["math", "math"])), 400, "Duplicate subjects rejected");
Check(BillingRules.NextPeriod(new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc), 31).Day == 28, "Short month billing date");
Check(BillingRules.NextPeriod(new DateTime(2026, 2, 28, 10, 0, 0, DateTimeKind.Utc), 31).Day == 31, "Original billing day preserved");
Check(BillingRules.Refundable(now.AddDays(-30), now) && !BillingRules.Refundable(now.AddDays(-30).AddSeconds(-1), now), "30-day refund boundary");
Check(PayGateClient.Checksum(["10011072130", "pgtest_123456789", "3299", "ZAR", "https://my.return.url/page",
    "2018-01-01 12:00:00", "en-za", "ZAF", "customer@paygate.co.za"], "secret") == "59229d9c6cb336ae4bd287c87e6f0220", "Official PayWeb checksum vector");
await Reject(() => Task.FromResult(PayGateClient.ParseForm("REFERENCE=a&REFERENCE=b")), 400, "Duplicate provider fields rejected");
Check(!new PayGateOptions(new TestEnvironment("Production")).Enabled, "Unconfigured billing fails closed");

// These are synthetic fixture values, not account credentials.
Environment.SetEnvironmentVariable("PAYGATE_MODE", "sandbox");
Environment.SetEnvironmentVariable("PAYGATE_ID", "fixture-merchant");
Environment.SetEnvironmentVariable("PAYGATE_ENCRYPTION_KEY", "fixture-not-a-real-key");
Environment.SetEnvironmentVariable("PAYGATE_PAYHOST_PASSWORD", "fixture-not-a-real-password");
Environment.SetEnvironmentVariable("PAYGATE_APP_ORIGIN", "https://fixture.invalid");
Environment.SetEnvironmentVariable("PAYGATE_RECURRING_ENABLED", "true");
var development = new TestEnvironment("Development");
Environment.SetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX", "true");
Check(new PayGateOptions(development).PublicSandbox && new PayGateOptions(development).Enabled, "Public provider account is explicitly sandbox-only");
Check(!new PayGateOptions(new TestEnvironment("Production")).PublicSandbox && !new PayGateOptions(new TestEnvironment("Production")).Enabled, "Production rejects public sandbox account");
Environment.SetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX", "false");
var options = new PayGateOptions(development);
Check(options.Enabled && !new PayGateOptions(new TestEnvironment("Production")).Enabled, "Sandbox restricted to development");
var originalDb = new Database();
var schema = "billing_checks_" + Guid.NewGuid().ToString("N");
var privateKeys = Path.Combine(Path.GetTempPath(), schema);
Directory.CreateDirectory(privateKeys);
await using var owner = await originalDb.Open();
await owner.ExecuteAsync($"CREATE SCHEMA {schema}");
var config = originalDb.BackupEnvironment;
var cs = new NpgsqlConnectionStringBuilder
{
    Host = config["PGHOST"], Port = int.Parse(config["PGPORT"]), Database = config["PGDATABASE"],
    Username = config["PGUSER"], Password = config["PGPASSWORD"], SearchPath = schema,
    SslMode = config["PGSSLMODE"] == "require" ? SslMode.Require : SslMode.Prefer
};
Environment.SetEnvironmentVariable("DATABASE_URL", cs.ConnectionString);
try
{
    var db = new Database();
    await using var c = await db.Open();
    await c.ExecuteAsync("""
        CREATE TABLE guru_profiles(id text PRIMARY KEY,full_name text,email text,role text);
        CREATE TABLE guru_subjects(id text PRIMARY KEY,grades integer[]);
        CREATE TABLE guru_learners(id uuid PRIMARY KEY,parent_id text,name text,email text,grade integer);
        CREATE TABLE guru_courses(id uuid PRIMARY KEY,subject_id text,grade_min integer,grade_max integer,tutor_id text);
        CREATE TABLE guru_enrollments(id uuid PRIMARY KEY,parent_id text,learner_id uuid,subject_ids text[],
          monthly_amount_zar integer,status text DEFAULT 'pending',payment_status text DEFAULT 'awaiting_provider',
          period_ends_at timestamptz,created_at timestamptz DEFAULT now());
        INSERT INTO guru_profiles VALUES('parent','Billing Test Parent','parent@fixture.invalid','parent');
        INSERT INTO guru_subjects VALUES('math',ARRAY[11]),('science',ARRAY[11]);
        """);
    await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "billing.sql")));
    var learnerId = Guid.NewGuid();
    var id = Guid.NewGuid();
    var courseId = Guid.NewGuid();
    await c.ExecuteAsync("""
        INSERT INTO guru_learners VALUES(@learnerId,'parent','Learner','learner@fixture.invalid',11);
        INSERT INTO guru_courses VALUES(@courseId,'math',11,11,'owner-tutor');
        INSERT INTO guru_enrollments(id,parent_id,learner_id,subject_ids,monthly_amount_zar)
          VALUES(@id,'parent',@learnerId,ARRAY['math'],290);
        """, new { learnerId, id, courseId });
    var fixture = new PayGateFixture();
    var client = new PayGateClient(new FixtureClients(fixture), options);
    var service = new BillingService(db, client, options, DataProtectionProvider.Create(new DirectoryInfo(privateKeys)));
    var identityServices = new ServiceCollection();
    identityServices.AddLogging();
    identityServices.AddSingleton(db);
    identityServices.AddIdentityCore<IdentityUser>().AddUserStore<DapperUserStore>();
    using var identityProvider = identityServices.BuildServiceProvider();
    var identityUsers = identityProvider.GetRequiredService<UserManager<IdentityUser>>();
    var domain = new Tutoring(db, development, identityUsers);
    HttpContext Context(string role, string profile, string email)
    {
        var context = new DefaultHttpContext();
        context.Items["profileId"] = profile;
        context.Items["profile"] = new Dictionary<string, object?> { ["role"] = role, ["email"] = email };
        return context;
    }
    var parent = Context("parent", "parent", "parent@fixture.invalid");
    var learner = Context("learner", "learner-profile", "learner@fixture.invalid");
    var otherParent = Context("parent", "other-parent", "other@fixture.invalid");
    var otherLearner = Context("learner", "other-learner", "other@fixture.invalid");
    Check((await domain.Accessible(parent)).Length == 0, "Pending payment grants no lesson access");
    await Reject(() => service.Checkout(id, "other-parent", Guid.NewGuid(), true), 404, "Other parent cannot pay this subscription");
    await Reject(() => service.Checkout(id, "parent", Guid.NewGuid(), false), 400, "Recurring consent required");
    var key = Guid.NewGuid();
    var forms = await Task.WhenAll(service.Checkout(id, "parent", key, true), service.Checkout(id, "parent", key, true));
    Check(forms[0].PaymentId == forms[1].PaymentId && fixture.Initiations == 1, "Concurrent duplicate checkout initiates once");
    Check(forms[0].Fields.Count == 2 && forms[0].ActionUrl == PayGateClient.ProcessUrl, "Hosted checkout exposes only signed redirect fields");
    Check((await service.RecoverCheckout(id, "parent")).PaymentId == forms[0].PaymentId && fixture.Initiations == 1, "Pending checkout survives navigation without a new charge");
    await c.ExecuteAsync("UPDATE guru_billing_accounts SET merchant_id='other-fixture-merchant' WHERE enrollment_id=@id", new { id });
    await Reject(() => service.RecoverCheckout(id, "parent"), 409, "Merchant accounts cannot be mixed even in the same mode");
    await c.ExecuteAsync("UPDATE guru_billing_accounts SET merchant_id=@merchant WHERE enrollment_id=@id", new { id, merchant = options.MerchantId });
    await Reject(() => service.Checkout(id, "parent", Guid.NewGuid(), true), 409, "Unresolved checkout blocks duplicate charge");
    Check((await domain.Accessible(learner)).Length == 0, "Checkout creation does not grant learner access");
    var p = await c.QuerySingleAsync("SELECT reference,request_id FROM guru_billing_payments WHERE id=@id", new { id = forms[0].PaymentId });
    var fields = fixture.Notification((string)p.reference, (string)p.request_id);
    fields["CHECKSUM"] = "00000000000000000000000000000000";
    await Reject(() => service.Notify(fields), 400, "Forged notification rejected");
    await service.Notify(fixture.Notification((string)p.reference, (string)p.request_id));
    var end = await c.QuerySingleAsync<DateTime>("SELECT period_ends_at FROM guru_enrollments WHERE id=@id", new { id });
    await service.Notify(fixture.Notification((string)p.reference, (string)p.request_id));
    Check(await c.QuerySingleAsync<DateTime>("SELECT period_ends_at FROM guru_enrollments WHERE id=@id", new { id }) == end, "Duplicate callback never extends period twice");
    Check((await domain.Accessible(parent)).Contains(courseId) && (await domain.Accessible(learner)).Contains(courseId), "Confirmed payment unlocks parent and linked learner");
    Check((await domain.Accessible(otherParent)).Length == 0 && (await domain.Accessible(otherLearner)).Length == 0, "Unrelated accounts remain denied");
    Check((await new Tutoring(db, new TestEnvironment("Production"), identityUsers).Accessible(parent)).Length == 0, "Sandbox payment never unlocks production lessons");
    var cipher = await c.QuerySingleAsync<string>("SELECT vault_cipher FROM guru_billing_accounts WHERE enrollment_id=@id", new { id });
    Check(!cipher.Contains("fixture-vault"), "Card token encrypted at rest");
    await service.Change(id, "parent", new EnrollmentUpdate("change", ["math", "science"]), domain);
    Check(await c.QuerySingleAsync<int>("SELECT monthly_amount_zar FROM guru_enrollments WHERE id=@id", new { id }) == 290, "Paid plan changes do not change current entitlement");
    await service.Change(id, "parent", new EnrollmentUpdate("cancel", null), domain);
    Check(!await c.QuerySingleAsync<bool>("SELECT auto_renew FROM guru_billing_accounts WHERE enrollment_id=@id", new { id }), "Cancellation stops future renewals");
    Check((await domain.Accessible(parent)).Contains(courseId), "Cancellation preserves paid-period access");
    await service.Change(id, "parent", new EnrollmentUpdate("resume", null), domain);
    var due = DateTime.UtcNow.AddMinutes(-5);
    await c.ExecuteAsync("UPDATE guru_enrollments SET period_ends_at=@due WHERE id=@id", new { due, id });
    Check((await domain.Accessible(learner)).Length == 0, "Expired period blocks lesson access immediately");
    await Task.WhenAll(service.RenewDue(id), service.RenewDue(id));
    Check(fixture.Renewals == 1, "Concurrent renewal workers charge a period only once");
    Check(await c.QuerySingleAsync<int>("SELECT monthly_amount_zar FROM guru_enrollments WHERE id=@id", new { id }) == 495, "Scheduled two-subject plan billed at R495");
    Check((await domain.Accessible(learner)).Contains(courseId), "Successful renewal restores access");
    var renewalPayment = await c.QuerySingleAsync<Guid>("SELECT id FROM guru_billing_payments WHERE enrollment_id=@id AND kind='renewal'", new { id });
    fixture.RejectRefund = true;
    await service.Refund(id, "parent", renewalPayment, Guid.NewGuid());
    Check(await c.QuerySingleAsync<bool>("SELECT retry_safe FROM guru_billing_payments WHERE original_payment_id=@renewalPayment", new { renewalPayment }),
        "Original refund authentication rejection proves no refund was processed");
    fixture.RejectRefund = false;
    await service.Refund(id, "parent", renewalPayment, Guid.NewGuid());
    Check(await c.QuerySingleAsync<int>("SELECT count(*) FROM guru_billing_payments WHERE original_payment_id=@renewalPayment AND state='succeeded'", new { renewalPayment }) == 1,
        "Only a definitive pre-processing rejection permits one new refund attempt");
    var refundsBeforeOriginal = fixture.Refunds;
    var original = forms[0].PaymentId;
    var refundKey = Guid.NewGuid();
    await Task.WhenAll(service.Refund(id, "parent", original, refundKey), service.Refund(id, "parent", original, refundKey));
    Check(fixture.Refunds == refundsBeforeOriginal + 1, "Concurrent refund requests return money only once");
    Check(await c.QuerySingleAsync<string>("SELECT payment_status FROM guru_enrollments WHERE id=@id", new { id }) == "refunded", "Confirmed refund recorded");
    Check((await domain.Accessible(learner)).Length == 0, "Confirmed refund ends lesson access");
    await service.Notify(fixture.Notification((string)p.reference, (string)p.request_id));
    Check((await domain.Accessible(learner)).Length == 0, "Old successful callback cannot undo refund");
    await Reject(() => service.Refund(id, "other-parent", original, Guid.NewGuid()), 404, "Other parent cannot request refund");

    // A fresh subscription permits independent failure and uncertain-outcome checks.
    async Task<Guid> Fresh()
    {
        var fresh = Guid.NewGuid();
        await c.ExecuteAsync("INSERT INTO guru_enrollments(id,parent_id,learner_id,subject_ids,monthly_amount_zar) VALUES(@fresh,'parent',@learnerId,ARRAY['math'],290)", new { fresh, learnerId });
        return fresh;
    }
    var failedId = await Fresh();
    var failedForm = await service.Checkout(failedId, "parent", Guid.NewGuid(), true);
    var failed = await c.QuerySingleAsync("SELECT reference,request_id FROM guru_billing_payments WHERE id=@id", new { id = failedForm.PaymentId });
    fixture.Statuses[(string)failed.reference] = "2";
    await service.Reconcile(failedId, "parent");
    Check(await c.QuerySingleAsync<string>("SELECT payment_status FROM guru_enrollments WHERE id=@id", new { id = failedId }) == "failed", "Provider decline marks subscription failed");
    await service.Checkout(failedId, "parent", Guid.NewGuid(), true);
    Check(fixture.Initiations == 3, "Confirmed failure allows a new customer checkout");

    var uncertainId = await Fresh();
    var uncertainForm = await service.Checkout(uncertainId, "parent", Guid.NewGuid(), true);
    await service.Reconcile(uncertainId, "parent");
    await c.ExecuteAsync("UPDATE guru_enrollments SET period_ends_at=now()-interval '5 minutes' WHERE id=@id", new { id = uncertainId });
    fixture.TimeoutRenewal = true;
    var renewalCount = fixture.Renewals;
    try { await service.RenewDue(uncertainId); } catch (HttpRequestException) { }
    await service.RenewDue(uncertainId);
    Check(fixture.Renewals == renewalCount + 1, "Timeout never causes a second renewal charge");
    await Reject(() => service.Checkout(uncertainId, "parent", Guid.NewGuid(), true), 409, "Uncertain charge blocks hosted retry");
    await Reject(() => service.Change(uncertainId, "parent", new EnrollmentUpdate("cancel", null), domain), 409, "Uncertain charge cannot be hidden by cancellation");
    fixture.RejectQuery = true;
    await service.Reconcile(uncertainId, "parent");
    Check(await c.QuerySingleAsync<string>("SELECT state FROM guru_billing_payments WHERE enrollment_id=@id AND kind='renewal'", new { id = uncertainId }) == "uncertain",
        "A rejected status query never makes a lost charge safe to retry");
    fixture.RejectQuery = false;
    await service.Reconcile(uncertainId, "parent");
    Check((await c.QueryAsync<string>("SELECT state FROM guru_billing_payments WHERE enrollment_id=@id AND kind='renewal'", new { id = uncertainId })).Single() == "succeeded", "Query reconciles lost renewal response without charging again");
    Console.WriteLine($"All {checks} billing checks passed. No requests were sent to PayGate.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await owner.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
    Directory.Delete(privateKeys, true);
}

sealed class TestEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "BillingChecks";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class FixtureClients(PayGateFixture fixture) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(fixture, false) { Timeout = TimeSpan.FromSeconds(5) };
}
sealed class PayGateFixture : HttpMessageHandler
{
    public int Initiations, Renewals, Refunds;
    public bool TimeoutRenewal, RejectRefund, RejectQuery;
    public Dictionary<string, string> Statuses = new();
    private readonly Dictionary<string, Dictionary<string, string>> results = new();
    private readonly Dictionary<string, Dictionary<string, string>> refunds = new();
    public Dictionary<string, string> Notification(string reference, string requestId)
    {
        var r = new Dictionary<string, string>(results[reference]);
        r["TRANSACTION_STATUS"] = Statuses.GetValueOrDefault(reference, "1");
        r["RESULT_CODE"] = r["TRANSACTION_STATUS"] == "1" ? "990017" : "900003";
        r["CHECKSUM"] = PayGateClient.Checksum(r.Values, "fixture-not-a-real-key");
        return r;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host != "secure.paygate.co.za") throw new Exception("Unexpected provider endpoint.");
        var body = await request.Content!.ReadAsStringAsync(ct);
        Dictionary<string, string> Result(string reference, string requestId, string amount) => new()
        {
            ["PAYGATE_ID"] = "fixture-merchant", ["PAY_REQUEST_ID"] = requestId, ["REFERENCE"] = reference,
            ["TRANSACTION_STATUS"] = "1", ["RESULT_CODE"] = "990017", ["AUTH_CODE"] = "fixture",
            ["CURRENCY"] = "ZAR", ["AMOUNT"] = amount, ["RESULT_DESC"] = "Auth Done",
            ["TRANSACTION_ID"] = Guid.NewGuid().ToString("N"), ["RISK_INDICATOR"] = "AX",
            ["PAY_METHOD"] = "CC", ["PAY_METHOD_DETAIL"] = "Visa", ["VAULT_ID"] = "fixture-vault"
        };
        string Encode(Dictionary<string, string> data) => string.Join("&", data.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        if (request.RequestUri.AbsolutePath.Contains("payweb3"))
        {
            var fields = PayGateClient.ParseForm(body);
            if (!PayGateClient.ValidChecksum(fields, "fixture-not-a-real-key")) throw new Exception("Request checksum mismatch.");
            var reference = fields["REFERENCE"];
            Dictionary<string, string> response;
            if (request.RequestUri.AbsolutePath.EndsWith("initiate.trans"))
            {
                Initiations++;
                if (fields["VAULT"] != "1" || fields["PAY_METHOD"] != "CC") throw new Exception("Missing card mandate parameters.");
                var requestId = Guid.NewGuid().ToString().ToUpperInvariant();
                results[reference] = Result(reference, requestId, fields["AMOUNT"]);
                response = new() { ["PAYGATE_ID"] = "fixture-merchant", ["PAY_REQUEST_ID"] = requestId, ["REFERENCE"] = reference };
                response["CHECKSUM"] = PayGateClient.Checksum(response.Values, "fixture-not-a-real-key");
            }
            else response = Notification(reference, fields["PAY_REQUEST_ID"]);
            return new(HttpStatusCode.OK) { Content = new StringContent(Encode(response)) };
        }
        XNamespace pg = "http://www.paygate.co.za/PayHOST";
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var xml = XDocument.Parse(body);
        var payload = xml.Descendants(soap + "Body").Single().Elements().Single().Elements().Single();
        string Value(string name) => payload.Descendants(pg + name).First().Value;
        if (payload.Name.LocalName == "RefundRequest" && RejectRefund || payload.Name.LocalName == "QueryRequest" && RejectQuery)
        {
            var rejectedStatus = new XElement(pg + "Status", new XElement(pg + "StatusName", "Error"), new XElement(pg + "ResultCode", "11"));
            var rejectedResponse = new XElement(pg + (payload.Name.LocalName == "RefundRequest" ? "RefundResponse" : "QueryResponse"), rejectedStatus);
            var rejected = new XDocument(new XElement(soap + "Envelope", new XElement(soap + "Body",
                new XElement(pg + "SingleFollowUpResponse", rejectedResponse))));
            return new(HttpStatusCode.OK) { Content = new StringContent(rejected.ToString(), Encoding.UTF8, "text/xml") };
        }
        Dictionary<string, string> r;
        string responseType;
        if (payload.Name.LocalName == "CardPaymentRequest")
        {
            Renewals++;
            if (Value("InitiatedType") != "MERCHANT" || Value("RecurringTransactionType") != "RECURRING" || Value("VaultId") != "fixture-vault" ||
                payload.Descendants(pg + "CVV").Any() || payload.Descendants(pg + "CardNumber").Any())
                throw new Exception("Unsafe recurring card request.");
            var reference = Value("MerchantOrderId");
            r = results[reference] = Result(reference, Guid.NewGuid().ToString(), Value("Amount"));
            responseType = "CardPaymentResponse";
            if (TimeoutRenewal) throw new HttpRequestException("Simulated lost response after charge.");
        }
        else if (payload.Name.LocalName == "RefundRequest")
        {
            Refunds++;
            var original = results.Values.Single(p => p["TRANSACTION_ID"] == Value("TransactionId"));
            r = refunds[original["REFERENCE"]] = Result(original["REFERENCE"], original["PAY_REQUEST_ID"], Value("Amount"));
            r["TRANSACTION_STATUS"] = "4";
            r["RESULT_CODE"] = "990012";
            responseType = "RefundResponse";
        }
        else
        {
            var reference = Value("MerchantOrderId");
            r = Value("TransactionType") == "Refund" ? refunds[reference] : results[reference];
            responseType = "QueryResponse";
        }
        var status = new XElement(pg + "Status",
            new XElement(pg + "TransactionId", r["TRANSACTION_ID"]), new XElement(pg + "Reference", r["REFERENCE"]),
            new XElement(pg + "PayRequestId", r["PAY_REQUEST_ID"]), new XElement(pg + "TransactionStatusCode", r["TRANSACTION_STATUS"]),
            new XElement(pg + "ResultCode", r["RESULT_CODE"]), new XElement(pg + "Currency", r["CURRENCY"]),
            new XElement(pg + "Amount", r["AMOUNT"]), new XElement(pg + "VaultId", "fixture-vault"));
        var result = new XDocument(new XElement(soap + "Envelope", new XElement(soap + "Body",
            new XElement(pg + (payload.Name.LocalName == "CardPaymentRequest" ? "SinglePaymentResponse" : "SingleFollowUpResponse"),
                new XElement(pg + responseType, status)))));
        return new(HttpStatusCode.OK) { Content = new StringContent(result.ToString(), Encoding.UTF8, "text/xml") };
    }
}
