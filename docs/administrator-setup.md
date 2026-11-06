# Administrator setup

The administration screen is `/admin`. Administrator privileges cannot be selected during public signup or onboarding.

## Production provisioning

Set these production configuration values through the workspace's secure settings:

- `ADMIN_BOOTSTRAP_EMAIL`: the account owner's email address.
- `ADMIN_BOOTSTRAP_PASSWORD`: a new, strong password entered through Secrets, never code or chat.
- `ADMIN_BOOTSTRAP_ENABLED`: `true`, only for the initial provisioning publish.

On the next production startup, the app provisions the named administrator using ASP.NET Core Identity password hashing. This explicitly provisioned account is confirmed by the platform owner and can sign in without public email delivery. Other production registrations still require configured email delivery and verification.

Provisioning does not reset any existing password. If an account already exists, promotion requires the supplied password to match that account. Existing administrators are left unchanged. Legacy profile ownership is not transferred by matching an email address.

After confirming administrator sign-in, disable `ADMIN_BOOTSTRAP_ENABLED` and remove the initial bootstrap password from deployment configuration. Subsequent account changes must use normal authenticated flows.

The Identity and domain database schemas must already be provisioned. Startup does not execute migrations. Provisioning failures are logged without email addresses, credentials, password hashes, or verification tokens; the existing site continues serving.

### Safe failure diagnostics

Startup and `--bootstrap-admin` report the same sanitized failure categories:

- `MissingEmailConfiguration`, `InvalidEmailConfiguration`, or `MissingPasswordConfiguration`: correct the named production setting through secure settings.
- `ExistingAccountPasswordMismatch`: the supplied password does not match the existing account; no password is overwritten.
- `IdentityAccountCreationFailed`, `IdentityConfirmationFailed`, or `IdentitySecurityStampUpdateFailed`: inspect the accompanying allowlisted `IdentityCodes` (for example, `PasswordTooShort` or `PasswordRequiresDigit`). Unknown codes become `OtherIdentityError`; Identity descriptions are never logged.
- `DatabaseFailure`: includes only the PostgreSQL SQLSTATE, not the database message or query.
- `DatabaseConnectionFailure` or `UnexpectedFailure`: no connection details or exception messages are logged.

The CLI exits with status 1 on failure. Automatic production setup still leaves the site serving on failure. These diagnostics do not change password requirements, account privileges, or production email verification, and do not retry failed provisioning or database operations.

## Development check

The explicit CLI command `--bootstrap-admin` uses the same provisioner. Supply synthetic test credentials through that process's environment, not command arguments; do not commit or log them. Automatic bootstrap runs only in Production with the enable flag.

## Access

Administrators can inspect platform accounts, suspend or restore non-admin access, edit courses and tutor assignments, view subscriptions, and cancel subscriptions through the existing billing service. They can view all course resources. No manual paid-status or refund toggles are exposed. Normal users cannot grant themselves administrator access.

Administrator changes emit credential-free operational audit log entries. Suspension immediately blocks protected API access, revokes the security stamp, and blocks new sign-ins. Administrator accounts cannot be locked through the account list.
