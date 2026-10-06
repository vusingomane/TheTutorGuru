# TheTutorGuru

A South African high-school tutoring platform for parents, learners, and tutors.

## Run & Operate

- `pnpm --filter @workspace/api-server run dev` — run the API server (port 5000)
- `pnpm run typecheck` — full typecheck across all packages
- `pnpm run build` — typecheck + build all packages
- `pnpm --filter @workspace/api-spec run codegen` — regenerate API hooks and Zod schemas from the OpenAPI spec
- `pnpm --filter @workspace/db run push` — push DB schema changes (dev only)
- Required env: `DATABASE_URL` — Postgres connection string

## Stack

- pnpm workspaces, Node.js 24, TypeScript 5.9
- API: Express 5
- DB: PostgreSQL + Drizzle ORM
- Validation: Zod (`zod/v4`), `drizzle-zod`
- API codegen: Orval (from OpenAPI spec)
- Build: esbuild (CJS bundle)

## Where things live

- Web app: `artifacts/the-tutor-guru`; frontend routes are in `src/pages`.
- API contract: `lib/api-spec/openapi.yaml`; regenerate clients after any contract change.
- Database schema: `lib/db/src/schema/tutoring.ts`.
- Domain routes: `artifacts/api-server/src/routes/accounts.ts`, `classes.ts`, and `resources.ts`.
- Clerk and App Storage are provisioned; their secrets must never be exposed.

## Architecture decisions

- This first runnable MVP uses the provided React/Express scaffold. The source brief's .NET Minimal API, MudBlazor, Dapper, and ASP.NET Core Identity architecture is not delivered by this implementation and must not be described as fulfilled.
- Account roles are chosen at onboarding and cannot be changed by profile edits. Tutors manage only their own courses.
- A learner is linked to a parent's learner record through the authenticated account's primary email address. Course access requires a paid enrolment with an unexpired billing period, or tutor ownership.
- Uploads go directly to private App Storage. The server verifies upload ownership and metadata before attaching files; file delivery checks course access and supports byte ranges for recordings.
- No payment provider is connected. Requests remain pending and grant no lesson access. Paid billing changes are blocked rather than pretending to cancel an external recurring charge.

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
- Apply development schema changes with the DB package's push script; never perform startup-time DDL.
- Catalogue seed courses belong to a system catalogue profile, not to a signed-in tutor. New tutors create their own courses.
- The first build has static/type checks and a public preview check, but no automated signed-in browser pass.

## Pointers

- See the `pnpm-workspace` skill for workspace structure, TypeScript setup, and package details
