---
name: Preview email verification
description: Scope of the user-approved email-verification bypass for development testing.
---

Allow unverified accounts to sign in and complete their profiles in the development preview only. Require email verification on the published app. Do not falsely mark addresses as verified.

**Why:** The user approved “Enable preview-only bypass” so they can test accounts without receiving development verification emails.

**How to apply:** Preserve this preview-only scope when changing authentication or adding real email delivery. This is not permission to disable verification in production or to bypass account-migration ownership checks.
