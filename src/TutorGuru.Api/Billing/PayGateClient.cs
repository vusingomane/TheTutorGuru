using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.WebUtilities;

namespace TutorGuru.Api.Billing;

public sealed class PayGateOptions(IHostEnvironment environment)
{
    public string Mode { get; } = Environment.GetEnvironmentVariable("PAYGATE_MODE") ?? "disabled";
    // Public, documented PayGate testing preset; these are not a merchant's private credentials.
    // Opt-in is Development+sandbox only. Never fall back to this account on a real merchant failure.
    public bool PublicSandbox { get; } = environment.IsDevelopment()
        && Environment.GetEnvironmentVariable("PAYGATE_MODE") == "sandbox"
        && Environment.GetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX") == "true";
    public string MerchantId { get; } = environment.IsDevelopment()
        && Environment.GetEnvironmentVariable("PAYGATE_MODE") == "sandbox"
        && Environment.GetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX") == "true"
        ? Environment.GetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX_ACCOUNT") ?? "10011064270" : Environment.GetEnvironmentVariable("PAYGATE_ID") ?? "";
    public string EncryptionKey { get; } = environment.IsDevelopment()
        && Environment.GetEnvironmentVariable("PAYGATE_MODE") == "sandbox"
        && Environment.GetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX") == "true"
        ? "secret" : Environment.GetEnvironmentVariable("PAYGATE_ENCRYPTION_KEY") ?? "";
    public string Password { get; } = environment.IsDevelopment()
        && Environment.GetEnvironmentVariable("PAYGATE_MODE") == "sandbox"
        && Environment.GetEnvironmentVariable("PAYGATE_PUBLIC_SANDBOX") == "true"
        ? "test" : Environment.GetEnvironmentVariable("PAYGATE_PAYHOST_PASSWORD") ?? "";
    public string Origin { get; } = (Environment.GetEnvironmentVariable("PAYGATE_APP_ORIGIN") ?? "").TrimEnd('/');
    public bool Enabled => Mode is "sandbox" or "live" && MerchantId.Length > 0 && EncryptionKey.Length > 0 && Password.Length > 0
        && Uri.TryCreate(Origin, UriKind.Absolute, out var u) && u.Scheme == "https" && u.AbsolutePath == "/"
        && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment) && string.IsNullOrEmpty(u.UserInfo)
        && Environment.GetEnvironmentVariable("PAYGATE_RECURRING_ENABLED") == "true"
        && (!PublicSandbox || MerchantId is "10011072130" or "10011064270")
        && !(Mode == "sandbox" && !environment.IsDevelopment())
        && !(Mode == "live" && MerchantId is "10011072130" or "10011064270" or "10011013800");
    public void Require()
    {
        if (!Enabled) throw new HttpError(503, "PayGate is not configured for secure recurring payments. No charge has been made.");
    }
}

public record PayGateResult(string Status, string ResultCode, string? TransactionId, string? RequestId,
    string? Reference, string? Currency, int? AmountCents, string? VaultId = null, bool RejectedBeforeProcessing = false);
public record BillingCheckout(Guid PaymentId, string ActionUrl, Dictionary<string, string> Fields);

// Fixed HTTPS endpoints: credentials/tokens never leave the backend, no arbitrary endpoint overrides.
public sealed class PayGateClient(IHttpClientFactory clients, PayGateOptions options)
{
    public const string ProcessUrl = "https://secure.paygate.co.za/payweb3/process.trans";
    private const string HostUrl = "https://secure.paygate.co.za/payhost/process.trans";
    private static readonly XNamespace Pg = "http://www.paygate.co.za/PayHOST";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    public static string Checksum(IEnumerable<string> values, string key)
        => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(string.Concat(values) + key)));
    public static Dictionary<string, string> ParseForm(string raw)
    {
        var parsed = QueryHelpers.ParseQuery(raw);
        if (parsed.Count > 40 || parsed.Any(p => p.Value.Count != 1))
            throw new HttpError(400, "Invalid PayGate field count.");
        return parsed.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
    }
    public static bool ValidChecksum(Dictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue("CHECKSUM", out var received) || received.Length != 32) return false;
        var computed = Checksum(fields.Where(p => p.Key != "CHECKSUM").Select(p => p.Value), key);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(received.ToLowerInvariant()));
    }
    public void Authenticate(Dictionary<string, string> fields)
    {
        options.Require();
        if (!ValidChecksum(fields, options.EncryptionKey) || fields.GetValueOrDefault("PAYGATE_ID") != options.MerchantId)
            throw new HttpError(400, "Invalid PayGate signature.");
    }
    public BillingCheckout CheckoutForm(BillingPayment payment) => new(payment.Id, ProcessUrl, new()
    {
        ["PAY_REQUEST_ID"] = payment.RequestId ?? throw new HttpError(409, "Checkout is awaiting provider confirmation. Refresh payment status before trying again."),
        ["CHECKSUM"] = Checksum([options.MerchantId, payment.RequestId!, payment.Reference], options.EncryptionKey)
    });
    private async Task<Dictionary<string, string>> FormRequest(string endpoint, Dictionary<string, string> data)
    {
        options.Require();
        data["CHECKSUM"] = Checksum(data.Values, options.EncryptionKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://secure.paygate.co.za/payweb3/" + endpoint)
        { Content = new FormUrlEncodedContent(data) };
        request.Headers.Referrer = new Uri(options.Origin);
        using var response = await clients.CreateClient("paygate").SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new HttpError(503, "PayGate did not confirm the request. Refresh payment status; do not retry the charge.");
        var fields = ParseForm(await response.Content.ReadAsStringAsync());
        Authenticate(fields);
        return fields;
    }
    public async Task<string> Initiate(BillingPayment p, string email)
    {
        var response = await FormRequest("initiate.trans", new()
        {
            ["PAYGATE_ID"] = options.MerchantId, ["REFERENCE"] = p.Reference,
            ["AMOUNT"] = p.AmountCents.ToString(CultureInfo.InvariantCulture), ["CURRENCY"] = "ZAR",
            ["RETURN_URL"] = options.Origin + "/api/billing/paygate/return",
            ["TRANSACTION_DATE"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["LOCALE"] = "en-za", ["COUNTRY"] = "ZAF", ["EMAIL"] = email,
            ["PAY_METHOD"] = "CC", ["PAY_METHOD_DETAIL"] = "",
            ["NOTIFY_URL"] = options.Origin + "/api/billing/paygate/notify",
            ["VAULT"] = "1"
        });
        if (response.GetValueOrDefault("REFERENCE") != p.Reference || !Guid.TryParse(response.GetValueOrDefault("PAY_REQUEST_ID"), out var id))
            throw new HttpError(503, "PayGate did not return a verified checkout reference.");
        return response["PAY_REQUEST_ID"];
    }
    public async Task<PayGateResult> QueryWeb(BillingPayment p)
    {
        var r = await FormRequest("query.trans", new()
        {
            ["PAYGATE_ID"] = options.MerchantId, ["PAY_REQUEST_ID"] = p.RequestId!, ["REFERENCE"] = p.Reference
        });
        return new(r.GetValueOrDefault("TRANSACTION_STATUS") ?? "", r.GetValueOrDefault("RESULT_CODE") ?? "",
            r.GetValueOrDefault("TRANSACTION_ID"), r.GetValueOrDefault("PAY_REQUEST_ID"), r.GetValueOrDefault("REFERENCE"),
            r.GetValueOrDefault("CURRENCY"), int.TryParse(r.GetValueOrDefault("AMOUNT"), out var amount) ? amount : null,
            r.GetValueOrDefault("VAULT_ID"));
    }
    private XElement E(string name, object? value) => new(Pg + name, value);
    private XElement Account() => E("Account", new[] { E("PayGateId", options.MerchantId), E("Password", options.Password) });
    private async Task<PayGateResult> Host(string wrapper, XElement payload, string responseType)
    {
        options.Require();
        var xml = new XDocument(new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XElement(Soap + "Body", new XElement(Pg + wrapper, payload))));
        using var request = new HttpRequestMessage(HttpMethod.Post, HostUrl) { Content = new StringContent(xml.ToString(), Encoding.UTF8, "text/xml") };
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var response = await clients.CreateClient("paygate").SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new HttpError(503, "PayGate has not confirmed the payment outcome.");
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100000 });
        var document = XDocument.Load(reader);
        var status = document.Descendants(Pg + responseType).SingleOrDefault()?.Element(Pg + "Status")
            ?? throw new HttpError(503, "PayGate returned an unconfirmed payment outcome.");
        string? S(string name) => status.Element(Pg + name)?.Value;
        return new(S("TransactionStatusCode") ?? "", S("ResultCode") ?? "", S("TransactionId"), S("PayRequestId"),
            S("Reference"), S("Currency"), int.TryParse(S("Amount"), out var amount) ? amount : null, S("VaultId"),
            S("StatusName") == "Error" && (S("ResultCode") is "11" or "12" ||
                S("ResultCode") == "15" && S("ResultDescription") == "Transaction cant be Merchant Initiated with 3D Enabled Terminal"));
    }
    public Task<PayGateResult> Renew(BillingPayment p, BillingEnrollment e, string vault)
    {
        var names = e.FullName.Split(' ', 2);
        var card = E("CardPaymentRequest", new[]
        {
            Account(), E("Customer", new[] { E("FirstName", names[0]), E("LastName", names.Length > 1 ? names[1] : names[0]), E("Email", e.Email) }),
            E("VaultId", vault), E("BudgetPeriod", 0),
            E("Order", new[] { E("MerchantOrderId", p.Reference), E("Currency", "ZAR"), E("Amount", p.AmountCents),
                E("TransactionDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) }),
            E("Recurring", new[] { E("RecurringTransactionType", "RECURRING"), E("InitiatedType", "MERCHANT") })
        });
        return Host("SinglePaymentRequest", card, "CardPaymentResponse");
    }
    public Task<PayGateResult> Refund(BillingPayment original)
        => Host("SingleFollowUpRequest", E("RefundRequest", new[]
        {
            Account(), E("TransactionId", original.TransactionId), E("Amount", original.AmountCents)
        }), "RefundResponse");
    public Task<PayGateResult> QueryHost(BillingPayment p, BillingPayment? original = null)
        => Host("SingleFollowUpRequest", E("QueryRequest", new[]
        {
            Account(), E("MerchantOrderId", original?.Reference ?? p.Reference),
            E("TransactionType", p.Kind == "refund" ? "Refund" : "Authorisation")
        }), "QueryResponse");
}
