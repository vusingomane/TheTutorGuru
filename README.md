# TheTutorGuru — C# / .NET

TheTutorGuru is a tutoring platform built with **.NET 10, ASP.NET Core Minimal APIs,
Blazor Server, MudBlazor, PostgreSQL, Dapper, and ASP.NET Core Identity**.

The current `main` branch contains the .NET application only. It does not require
React, Express, Node.js, npm, or pnpm. Earlier versions remain in Git history.
The small `wwwroot/guru.js` file is browser interop for secure API calls and file
uploads, not a React application.

## Solution

Open **`TheTutorGuru.slnx`** in an IDE with .NET 10 support.

```text
src/
  TutorGuru.Api/                  ASP.NET Core Minimal API, Identity, Dapper, PayGate
  TutorGuru.Web/                  Blazor Server and MudBlazor; same-origin API proxy
tests/
  TutorGuru.AuthPolicyChecks/     Standalone authentication policy checks
  TutorGuru.BillingChecks/        Billing checks with isolated PostgreSQL fixtures
database/
  001-schema.sql                 Empty PostgreSQL schema, without account data
  002-subjects.sql                Public subject catalogue
```

## Requirements

- .NET **10 SDK**, including the ASP.NET Core runtime.
- PostgreSQL **16 or newer** with current PostgreSQL command-line tools (`psql`).
  The schema was exported using PostgreSQL 16.10; use a current patched `psql`
  that supports its `\restrict` / `\unrestrict` commands.
- Git.
- An HTTPS development certificate:

```sh
dotnet dev-certs https --trust
dotnet restore TheTutorGuru.slnx
dotnet build TheTutorGuru.slnx
```

## 1. Create a local database

Create a **new, empty local database** and a PostgreSQL login for this app using
your PostgreSQL administrator tools. Never run these setup scripts against an
existing or production database.

For example, after creating a database named `thetutorguru`, apply:

```sh
psql -h localhost -U YOUR_POSTGRES_USER -d thetutorguru -W -v ON_ERROR_STOP=1 -f database/001-schema.sql
psql -h localhost -U YOUR_POSTGRES_USER -d thetutorguru -W -v ON_ERROR_STOP=1 -f database/002-subjects.sql
```

`-W` asks for your database password locally. No database rows or credentials from
the hosted app are included in this repository.

## 2. Configure and start the API

The API reads configuration from environment variables. Set them in your local
terminal or IDE's **private** run configuration. Do not commit actual values.

| Variable | Local setting |
|---|---|
| `DATABASE_URL` | `Host=localhost;Port=5432;Database=thetutorguru;Username=YOUR_USER;Password=YOUR_PASSWORD;SSL Mode=Disable` |
| `SESSION_SECRET` | A private random string of at least 32 characters; keep it stable to decrypt stored Identity keys |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
| `PORT` | `8080` |
| `PAYGATE_MODE` | `disabled` until you intentionally configure payments |

These are placeholders, not credentials. Environment variables set in one
terminal do not automatically appear in another terminal.

Start the API from the repository root:

```sh
dotnet run --project src/TutorGuru.Api --no-launch-profile
```

## 3. Start the Blazor website

In a **second terminal**, set `ASPNETCORE_ENVIRONMENT=Development`, then run:

```sh
dotnet run --project src/TutorGuru.Web --no-launch-profile
```

Open **https://localhost:7243**. The Blazor host proxies `/api/...` to the API at
`http://127.0.0.1:8080`; authentication cookies and CSRF requests stay same-origin.
Do not use plain HTTP for sign-in: Identity cookies are HTTPS-only.

To change the website URL, set `ASPNETCORE_URLS` on the **web process**.
To change the API destination, set
`ReverseProxy__Clusters__api__Destinations__local__Address` on the web process
and the corresponding `PORT` on the API.

## 4. Create the initial platform administrator

Using a terminal with the API's `DATABASE_URL` and `SESSION_SECRET` configured,
privately set `ADMIN_BOOTSTRAP_EMAIL` and `ADMIN_BOOTSTRAP_PASSWORD`. The password
must have at least 10 characters, uppercase and lowercase letters, a digit,
and a symbol. Then run:

```sh
dotnet run --project src/TutorGuru.Api --no-launch-profile -- --bootstrap-admin
```

Clear the bootstrap password from the terminal afterwards. Provisioning never
resets an existing password. The administrator can sign in at `/sign-in` and
manage the platform at `/admin`.

Public registration needs no Resend, SMTP, or email verification. New accounts
remain pending until an administrator selects **Accounts → Activate account**.
Activation does not grant an administrator role or confirm email ownership.

## Tests

```sh
dotnet run --project tests/TutorGuru.AuthPolicyChecks
```

The billing checks additionally need the API's development database configuration.
They create a temporary schema, use synthetic provider responses, and must run
only against a disposable local/development database:

```sh
dotnet run --project tests/TutorGuru.BillingChecks
```

## External services and limitations

- **File uploads and recordings:** the current `ObjectStorage` adapter uses
  Replit App Storage's local signing sidecar. That service does not exist on a
  normal computer. Local account, course, learner and attendance work does not
  require it, but file upload/download requires a storage adapter or a suitable
  hosted environment. Existing hosted files are not included in this clone.
- **Payments:** the PayGate integration is included but disabled unless configured.
  Do not enable live payments just to test the app. Billing configuration lives
  in `src/TutorGuru.Api/Billing/PayGateClient.cs`; use real merchant credentials
  only in secure environment settings.
- **Live classes:** meeting URLs use Zoom or Microsoft Teams. This repository
  does not itself host a conferencing service or automatically retrieve recordings.
- **Password recovery:** the existing email-based reset flow still needs SMTP.
  Registration and administrator activation do not need email delivery.
- **Legacy-account migration CLI:** backup/migration commands require Linux and
  PostgreSQL tools. They are not needed for a fresh local database initialized
  with the scripts above.

For deployment, run both .NET services behind HTTPS, forward `/api` to the API
and all other requests (including Blazor WebSockets) to the web host. Set
`ASPNETCORE_ENVIRONMENT=Production`. Provision schema changes through your
deployment/database process, not automatic startup DDL. Never commit secrets,
private database exports, account data, payment data, or private uploaded files.
