---
name: Administrator account activation
description: User-required administrator approval replacing email verification for registration.
---

Registration must not require Resend or email verification. New accounts require activation by a platform administrator before signing in, in both preview and production. Do not falsely mark addresses as verified.

**Why:** The user replaced the earlier preview-only verification bypass with “create the account without the verification only the the admin user can activate on the Platform Administrator”.

**How to apply:** Keep activation administrator-only; registration, email links, password resets, and unlocking must not activate an account. Preserve existing active accounts and keep activation separate from email ownership and suspension.
