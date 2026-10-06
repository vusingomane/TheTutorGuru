# TheTutorGuru architecture and safe account migration

## Active implementation

The user approved the original architecture after the account-migration
consequences were explained:

- **Frontend:** C# Blazor Server with MudBlazor, in `artifacts/the-tutor-guru/dotnet`.
- **API:** ASP.NET Core Minimal API, in `artifacts/api-server/dotnet`.
- **Persistence:** Dapper and Npgsql against the **existing PostgreSQL database**.
- **Authentication:** ASP.NET Core Identity, including its password hasher,
  email-confirmation/password-reset token providers, lockout, and security stamps.
  Its user store uses Dapper, not Entity Framework.
- **Files:** Existing private App Storage bucket and object paths, with access
  checked by the .NET API. Uploads remain direct-to-storage; downloads support ranges.

Both managed artifact workflows now use these implementations. React/Express
source remains under the original `src` directories for comparison and recovery.
It is not the active application. Legacy dependencies are retained deliberately;
removing the old source or dependencies is not part of this migration.

The API paths and response shapes from `lib/api-spec/openapi.yaml` remain
compatible. ASP.NET Core Identity adds `/api/auth/*` endpoints documented below.
Public `/api/subjects`, `/api/courses` and `/api/healthz` remain unauthenticated.

## Product invariants

- Mathematics: grades 8–12; Maths Literacy and Physical Science: 10–12;
  Afrikaans: 11–12.
- One subject: R290/month; two subjects: R495/month.
- Two live sessions weekly, one hour each, with the stated 30–180-learner,
  voice/text class format and full-refund-within-30-days policy.
- Parent, learner and tutor roles are chosen once.
- Pending/unpaid/expired enrolments do not grant lessons or file access.
- Tutors can manage only their own courses and private plans.
- Payment-provider management and automatic Teams/Zoom meeting/recording
  integration remain separate tasks. Nothing in this migration charges money,
  marks subscriptions paid automatically, or claims those integrations are live.

## Data safety

The migration does not drop or modify the existing domain tables, copy data
into a replacement database, rewrite legacy profile IDs, or move/delete files.
The additive schema contains Identity users, a unique Identity-to-profile mapping,
single-use invitation hashes, durable data-protection keys, and a private
development mail outbox.

Before the first additive migration, the operator tool creates a private
`pg_dump` backup and a row-fingerprint baseline under workspace `.data/`.
That directory is ignored by git, restricted to the operator, and **must not
be published or served as static files**. A development filesystem backup
is not a substitute for a verified production backup.
Published-layout operators can set `PRIVATE_MIGRATION_DIR` to an explicitly
protected, persistent backup directory; the tool must have `pg_dump` available.

```sh
dotnet build artifacts/api-server/dotnet/TutorGuru.Api.csproj
dotnet artifacts/api-server/dotnet/bin/Debug/net10.0/TutorGuru.Api.dll --migrate
dotnet artifacts/api-server/dotnet/bin/Debug/net10.0/TutorGuru.Api.dll --verify-preservation
```

The preservation check compares **every original row** with its original hash;
new records are allowed, changes/deletions to original records fail the check.
It does not verify object-byte backups; files remain in the original bucket.
No startup-time DDL is performed. The post-merge setup runs the explicit
additive migration in the development workspace, not against production.

## Existing Clerk account migration

Clerk accounts and sessions are not converted by copying password hashes.
Users register a new Identity password and verify their email. Existing
Clerk users must receive a one-use invitation before linking their profile.
Do not delete their Clerk accounts before acceptance of the migration.

1. Verify the owner of the existing profile through an authenticated legacy
   session or an equivalent trusted support process.
2. Issue an invitation, using the **existing profile ID**:

   ```sh
   dotnet artifacts/api-server/dotnet/bin/Debug/net10.0/TutorGuru.Api.dll \
     --issue-invitation <legacy-profile-id>
   ```

3. The invitation is written to a private `.data/invitation-*.json` file.
   Deliver it only to the verified owner over an appropriate private channel.
   It expires after seven days; only a hash is stored in the database.
4. The user registers at `/sign-up`, verifies email, signs in, then visits
   `/migrate`. The invitation email must match the **confirmed** Identity email.
5. The .NET API consumes the invitation and creates a unique profile link in
   one database transaction. It does not rewrite ownership foreign keys.
6. Verify the user's role, family or tutor courses, subscriptions, attendance
   and file access. Re-running an invitation cannot claim a second account.

A matching email alone never grants ownership of a legacy profile. Onboarding
blocks duplicate legacy profiles and points the user to the migration flow.
Existing parent–learner matching remains based on a confirmed learner email.

## Identity HTTP endpoints

JSON requests use `/api/auth`:

| Method/path | Body / response |
| --- | --- |
| GET `/csrf` | `{token}`; also sets the secure antiforgery cookie |
| GET `/me` | `{email,emailConfirmed}` or 401 |
| POST `/register` | `{email,password}`; sends verification, does not sign in |
| POST `/login` | `{email,password}`; verified accounts only |
| POST `/logout` | Ends the Identity session |
| POST `/confirm-email` | `{userId,token}` |
| POST `/resend-confirmation` | `{email}` |
| POST `/forgot-password` | `{email}`; generic response avoids account enumeration |
| POST `/reset-password` | `{userId,token,password}` |
| POST `/migrate` | `{code}`; authenticated, confirmed account required |

All non-GET API calls, including login, require `X-CSRF-TOKEN`. The browser
transport refreshes its token after login/logout. Passwords require at least
10 characters, mixed case, a digit and a non-alphanumeric character. Five
failed attempts lock an account for 15 minutes. Auth mutations are rate limited.

Identity and antiforgery cookies are Secure. Development allows SameSite=None
for embedded previews; production uses Lax/Strict. Only the local proxy is
trusted for forwarded IP/protocol headers. Request logging does not print
verification URLs, reset tokens, credentials, invitation codes, or signed URLs.

Data-protection keys are shared and persisted in PostgreSQL so Identity
sessions/tokens survive process restarts. Stored XML is protected with AES-GCM
using a key derived from `SESSION_SECRET`, held separately in workspace Secrets.
The migration encrypts any initial plaintext Identity key entries without
changing the underlying keys. Database access must still be tightly restricted.
Keep `SESSION_SECRET` stable and securely backed up; changing or losing it makes
existing keys unreadable. Secret rotation requires a coordinated key-rewrapping
process, not simply replacing the environment value.

## Email and production readiness

Development sends mail to `guru_identity_dev_mail`, a private operator mailbox
with **no HTTP retrieval endpoint**. This is not a public “skip verification”.
Only the workspace operator should retrieve the development verification/reset
link and deliver it to the intended tester. Never expose the outbox on a page.

Production fails explicitly if email delivery is missing. Configure `APP_ORIGIN`
to the **verified published app URL**, plus `SMTP_HOST`, `SMTP_PORT` (default
587), `SMTP_FROM`, `SMTP_USERNAME`, and `SMTP_PASSWORD`. Credentials belong in
Replit Secrets, not this document or source control. SMTP uses TLS.
`SESSION_SECRET` must contain at least 32 characters and remain stable.

Before publishing:

1. Obtain and verify a production backup and storage recovery plan.
2. Apply the additive Identity schema to the intended production database with
   explicit operator approval. Development accounts do not migrate themselves.
3. Configure production mail and the stable shared-key secret; send and verify real
   registration and recovery messages.
4. Plan a maintenance window for legacy writes. Do not leave two different
   account systems writing indefinitely to the same domain tables.
5. Validate migrated ownership and role journeys on a production-like copy.
6. Keep the old application and database backup until the cutover is accepted.

There has been **no production deployment or production-data migration** in
this task. Reverting application code does not undo newly written Identity
data; restore a database backup only with explicit approval and awareness of
any records created since that backup.

## Verification

`pnpm --filter @workspace/api-server run test:migration` runs request-level
checks using isolated development accounts/rows and cleans its fixtures by
default. It checks verification/recovery, permissions, prices/grades,
pending/expired access gates, single-use migration ownership, attendance,
real private uploads, deletion, and ranged downloads. Never point this
write-capable test suite at production.

Build both actual projects with their artifact package build scripts. The
old TypeScript typecheck remains available only to keep retained source valid;
it does not certify the .NET architecture. Browser verification is needed for
MudBlazor interaction and video playback.
