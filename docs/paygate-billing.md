# PayGate billing

## Readiness

The merchant's live PayGate account is **not connected**. At the user's request, development uses
PayGate's public testing account, enabled explicitly with `PAYGATE_PUBLIC_SANDBOX=true` and
`PAYGATE_MODE=sandbox`. The default mode remains disabled. This preset is rejected outside
ASP.NET Development and never serves as a fallback for failed merchant credentials.
Only public test cards may be used. Test subscriptions never unlock Production lessons.
The user's own live credentials and merchant readiness still need verification before production.

## Merchant products

The integration uses:

- **PayWeb3** hosted card checkout with **PayVault** tokenisation for the initial payment.
- **PayHost** merchant-initiated recurring token payments and full refunds.
- The app's persistent scheduler, not PaySubs. There is no separate provider subscription schedule
  to cancel. Cancellation stops the app's scheduler.

Have PayGate confirm that PayWeb3, PayVault, PayHost, merchant-initiated recurring transactions
without CVV, ZAR, and automatic settlement are enabled for the account. Confirm any acquirer-specific
initial recurring mandate requirements with PayGate before enabling live renewals.
No card numbers or CVV enter TheTutorGuru. Card tokens are encrypted with ASP.NET Data Protection.
Back up the existing Identity key store and keep SESSION_SECRET stable.

## Configuration

Use the workspace Secrets interface for `PAYGATE_ID`, `PAYGATE_ENCRYPTION_KEY`, and
`PAYGATE_PAYHOST_PASSWORD`. Never put these in chat, source, logs, screenshots, or this document.
The PayHost password may differ from the PayWeb3 encryption key; use the values assigned to the account.

Non-secret settings:

| Setting | Meaning |
| --- | --- |
| `PAYGATE_MODE` | `disabled` (default), `sandbox`, or `live` |
| `PAYGATE_APP_ORIGIN` | Explicit HTTPS origin, without a path, query, or fragment |
| `PAYGATE_RECURRING_ENABLED` | `true` only after merchant recurring capabilities are confirmed |
| `PAYGATE_PUBLIC_SANDBOX` | `true` opts into PayGate's published test account; Development+sandbox only |
| `PAYGATE_PUBLIC_SANDBOX_ACCOUNT` | Public preset account; defaults to the recurring-capable test terminal. Only published test IDs are accepted. |

Sandbox is restricted to ASP.NET Development. Known public PayGate test merchant IDs are rejected
in live mode. Sandbox-paid enrolments cannot unlock lesson access in Production.
Credentials are never returned by `/api/billing/config`.
Billing accounts and payment intents retain their actual merchant ID. Changing configured merchant
accounts never silently reuses a previous merchant's card token or queries its payments with new keys.
Use the published origin obtained through deployment tooling for production; development domains
are not production origins. Keep development and production credentials/settings separate.

## Explicit additive migration

Do not run DDL at startup and do not push the archived Drizzle schema.
Ensure the existing additive Identity migration in `docs/architecture-migration.md` has been applied
first; otherwise sign-in and CSRF requests fail because the Identity key tables are missing.
After a private backup and before enabling billing, run:

```sh
dotnet build artifacts/api-server/dotnet/TutorGuru.Api.csproj --nologo
dotnet artifacts/api-server/dotnet/bin/Debug/net10.0/TutorGuru.Api.dll --migrate-billing
```

The command retains a private database dump in `.data`, adds billing tables, and compares
existing domain row fingerprints. It does not change existing subscriptions or account ownership.
Apply it separately in production with the normal release process. Post-merge setup does not
automatically enable payment processing or apply this migration to production.

## Billing rules

- R290/month for one eligible subject, R495/month for two.
- A parent must explicitly consent to card storage and monthly billing.
- Checkout initiation or a browser return never grants lesson access.
- Authenticated notifications trigger a server-to-server status query. The confirmed amount,
  currency, reference and request ID must match the durable payment intent.
- Payment updates and entitlement updates are one database transaction.
- Duplicate callbacks never extend the paid period twice. Enrollment-scoped PostgreSQL locks
  serialize parent actions, callbacks and multiple worker instances.
- Paid subject/price changes apply at the next successful renewal, without mid-period proration.
- Cancellation stops future renewals and preserves the paid period. Resume is allowed only
  while a paid cancellation is still in effect and a verified card token exists.
- Refunds return the full amount of an individually confirmed payment within 30 days. Renewals stop
  as soon as the refund request is recorded. Only provider-confirmed refunds end paid access.
  A refund may take additional time to appear at the customer's bank.
- Declined payments stop automatic renewal. Expired periods grant no content access. A parent can
  use a new hosted checkout after a definitive decline.
- Missing card tokens disable renewal rather than silently assuming a usable mandate.
- PayHost refund confirmation is status `4` with result `990012` ("Refund done"), not the
  PayWeb card-authorisation success/decline interpretation. Only this matched full refund ends access.
- After an outage spanning an entire billing period, do not collect a backlog of old months;
  require a fresh customer checkout.

## Unknown outcomes and support

An external request intent is committed **before** it is sent. If a response is lost, the operation
remains `uncertain`; never repeat the charge/refund on a timeout. Parent **Refresh payment status**
and the worker query PayGate to recover the outcome. The ledger preserves the unique merchant
reference. A pending hosted checkout can be reopened after navigation without another initiation.

An initiation with a lost request ID, a provider query with no definitive result, a declined refund,
or acquirer-required authentication may require merchant support. Review the operation in the
PayGate portal using its merchant reference and reconcile the actual outcome. Never delete a ledger
entry or manually mark an enrolment paid to clear an error. Do not blindly retry refunds, even if
the customer's UI did not receive a response.
The sole self-service retry exception is a definitive authentication/configuration rejection of
the original refund submission: it proves no refund was processed. After correcting configuration,
a new request key may retry that rejected submission. An authentication error from a later status
query does not prove failure and never enables retry.

## Provider endpoints and validation

Callbacks are:

- `POST /api/billing/paygate/notify`: form-encoded, bounded input, checksum authentication, exact
  merchant/reference/amount/currency validation, independent provider query, plain-text `OK`.
- `POST /api/billing/paygate/return`: browser-only 303 redirect to subscriptions. It cannot settle
  payments. These two exact routes bypass cookie/CSRF requirements; parent billing actions do not.

Authoritative references:

- https://docs.paygate.co.za/paygate-by-network/reference/testing
- https://docs.paygate.co.za/paygate-by-network/reference/initiate-request
- https://docs.paygate.co.za/paygate-by-network/reference/security-and-checksum-validation
- https://docs.paygate.co.za/paygate-by-network/reference/notify-url-response
- https://docs.paygate.co.za/paygate-by-network/reference/query-transaction-status
- https://docs.paygate.co.za/paygate-by-network/reference/payhost-follow-up-requests
- https://secure.paygate.co.za/payhost/process.trans?wsdl

Use the actual WSDL for PayHost XML element names, ordering and SOAP endpoint. The generated
documentation's short examples are not a complete executable schema.
PayGate's general sandbox page and product testing page disagree about the public preset.
PayWeb3 and PayHost use different published public testing presets. PayHost's HTTP 200 response
can still contain an authentication error; inspect the SOAP status/result rather than treating HTTP
success as authentication. The public preset follows each product's own testing reference.
The general public test terminal can reject merchant-initiated renewals because 3-D Secure is
enabled. The recurring-capable public terminal is used for renewal verification. A definitive terminal
rejection of a submission stops renewal; it is not treated as a successful charge or retried blindly.

Checks:

```sh
pnpm --filter @workspace/api-server run test:billing
pnpm run typecheck
dotnet build artifacts/api-server/dotnet/TutorGuru.Api.csproj --nologo
dotnet build artifacts/the-tutor-guru/dotnet/TutorGuru.Web.csproj --nologo
```

`test:billing` never sends external payment requests. It covers prices, date/refund boundaries,
ownership, consent, checkout recovery, duplicate/concurrent operations, provider checksum validation,
failed and uncertain charges, encrypted card tokens, renewals, plan changes, cancellation, refunds,
and parent/learner/production lesson access. This does **not** replace sandbox integration verification.

Public-sandbox verification used synthetic Identity accounts and test cards only. Observed outcomes:

- Actual hosted R290 checkout was confirmed by the provider; consent was required.
- Pending and expired subscriptions denied shared lesson materials, including direct downloads.
- Paid parent and linked learner could download the tutor's shared PDF; unrelated accounts were denied.
  Private tutor preparation plans remained private.
- Scheduled two-subject pricing stayed pending during the R290 period. Cancellation preserved paid
  access, stopped renewal, and could be resumed before expiry.
- The recurring-capable public terminal confirmed one automatic R495 token renewal, not a fake paid
  update or a second customer checkout for the renewal itself.
- The initial test payment's full R290 refund was independently confirmed by PayHost and reconciled
  to refunded/cancelled with access blocked. A misread refund status was fixed without another refund.
- The R495 renewal's full refund also succeeded; refreshing status did not undo it or restore access.
  All synthetic test rows and the PDF object were removed only after final provider outcomes were known.

The 47 isolated checks cover declined payments, forged/duplicate notifications, concurrency, lost
responses and refund/date boundaries. Those negative cases are not claimed as live-bank browser
results. Live merchant configuration, acquirer approval and production cutover remain separate.
