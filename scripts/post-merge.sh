#!/bin/bash
set -euo pipefail
pnpm install --frozen-lockfile
dotnet build artifacts/api-server/dotnet/TutorGuru.Api.csproj --nologo
dotnet build artifacts/the-tutor-guru/dotnet/TutorGuru.Web.csproj --nologo
# Identity migration is explicit and additive; never push the archived Drizzle
# schema over Identity tables or automatically migrate production at startup.
dotnet artifacts/api-server/dotnet/bin/Debug/net10.0/TutorGuru.Api.dll --migrate
dotnet artifacts/api-server/dotnet/bin/Debug/net10.0/TutorGuru.Api.dll --verify-preservation
