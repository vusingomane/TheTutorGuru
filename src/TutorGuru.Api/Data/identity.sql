-- Additive, explicit migration. No legacy rows, tables or files are rewritten.
CREATE TABLE IF NOT EXISTS guru_identity_users (
    id text PRIMARY KEY,
    normalized_email text NOT NULL UNIQUE,
    normalized_username text NOT NULL UNIQUE,
    concurrency_stamp text NOT NULL,
    data jsonb NOT NULL
);
CREATE TABLE IF NOT EXISTS guru_identity_profile_links (
    identity_id text PRIMARY KEY REFERENCES guru_identity_users(id),
    profile_id text NOT NULL UNIQUE REFERENCES guru_profiles(id),
    linked_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS guru_identity_invitations (
    code_hash text PRIMARY KEY,
    profile_id text NOT NULL REFERENCES guru_profiles(id),
    email text NOT NULL,
    expires_at timestamptz NOT NULL,
    used_at timestamptz
);
CREATE TABLE IF NOT EXISTS guru_identity_keys (
    name text PRIMARY KEY,
    xml text NOT NULL
);
CREATE TABLE IF NOT EXISTS guru_identity_dev_mail (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    recipient text NOT NULL,
    subject text NOT NULL,
    link text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
