# TheTutorGuru

A South African high-school tutoring platform for parents, learners, and tutors.

## Run & Operate

- `pnpm --filter @workspace/api-server run dev` — ASP.NET Core Minimal API (managed port 8080)
- `pnpm --filter @workspace/the-tutor-guru run dev` — Blazor Server / MudBlazor web app
- `pnpm run typecheck` — retained TypeScript source checks (not .NET validation)
- `pnpm run build` — TypeScript check + .NET publish and other workspace builds
- `pnpm --filter @workspace/api-server run test:migration` — isolated development API/ownership/access checks
- `pnpm --filter @workspace/api-server run test:billing` — isolated billing ledger/rule/access checks with HTTP fixtures; no external charges
- `pnpm --filter @workspace/api-spec run codegen` — regenerate API hooks and Zod schemas from the OpenAPI spec
- Identity schema is applied explicitly by the .NET operator migration tool; see `docs/architecture-migration.md`. Do not push the archived Drizzle schema over Identity tables.
- Required env: `DATABASE_URL` and a stable `SESSION_SECRET` (32+ characters) protecting persisted Identity keys; never replace the latter casually.

## Stack

- .NET 10, C#, ASP.NET Core Minimal API, Blazor Server, MudBlazor
- Existing PostgreSQL database with Dapper/Npgsql, no Entity Framework
- ASP.NET Core Identity with a Dapper user store and persistent database keys
- pnpm scripts orchestrate the artifact-managed .NET services
- Original React/Express/Drizzle source and Orval clients retained for comparison/recovery, not actively served

## Where things live

- Web app: `artifacts/the-tutor-guru/dotnet`; Razor routes are in `Components/Pages`.
- API: `artifacts/api-server/dotnet`; domain rules in `Domain`, auth in `Identity`, private files in `Storage`.
- API contract: `lib/api-spec/openapi.yaml`; regenerate clients after any contract change.
- Existing domain schema reference: `lib/db/src/schema/tutoring.ts`; new additive Identity schema: `artifacts/api-server/dotnet/Data/identity.sql`.
- Original domain routes and React pages remain in their `src` folders as the migration baseline.
- App Storage remains provisioned. Clerk is retained only for the old source and safe account migration; active sign-in uses Identity. Never expose secrets or `.data/`.

## Architecture decisions

- The user explicitly approved replacing the MVP with the original .NET architecture after account/data consequences were explained. The active preview now uses that stack.
- Existing profile IDs and ownership references are preserved. Migration requires a confirmed Identity email and a one-use invitation; matching email alone cannot claim an existing profile.
- Development verification/reset emails go to a private operator mailbox. Production requires SMTP configuration and APP_ORIGIN before registration/recovery. See `docs/architecture-migration.md`.
- Account roles are chosen at onboarding and cannot be changed by profile edits. Tutors manage only their own courses.
- A learner is linked to a parent's learner record through the authenticated account's primary email address. Course access requires a paid enrolment with an unexpired billing period, or tutor ownership.
- Uploads go directly to private App Storage. The server verifies upload ownership and metadata before attaching files; file delivery checks course access and supports byte ranges for recordings.
- PayGate was selected because the user has a PayGate account. Billing uses PayWeb3/PayVault checkout and PayHost renewals/refunds, with an app-controlled schedule. The user requested the mock account: development explicitly uses PayGate's public sandbox preset, never live credentials or real funds. Production still defaults to disabled; see `docs/paygate-billing.md`.
- Billing schema changes are explicitly additive (`--migrate-billing`), not startup DDL. Never manually toggle a subscription to paid. Unknown charge/refund outcomes must be reconciled, not blindly retried.

## Product

- Public subject and pricing information, branded sign-in/sign-up, and role onboarding.
- Parent learner management and pending subscription requests.
- Tutor course creation, one-hour class scheduling, lesson plans, attendance, notes, and recording uploads.
- Protected learner materials, live meeting links, MP4 playback, and attendance-based progress reports.
- Teams/Zoom meeting links are entered manually. Meeting creation and automated recording retrieval are not integrated.

## User preferences

_Populate as you build — explicit user instructions worth remembering across sessions._

## Gotchas

- Run `pnpm --filter @workspace/api-spec run codegen` after contract changes.
- Use the existing artifact-managed workflows; do not start replacement dev servers.
- Never perform startup-time DDL. Use the explicit additive Identity migration with a private backup and original-row fingerprint verification.
- Catalogue seed courses belong to a system catalogue profile, not to a signed-in tutor. New tutors create their own courses.
- ASP.NET Core Identity and MudBlazor builds are the primary architecture checks. Keep the legacy TypeScript check passing, but do not confuse it with verifying .NET.

## Pointers

- See the `pnpm-workspace` skill for workspace structure, TypeScript setup, and package details
