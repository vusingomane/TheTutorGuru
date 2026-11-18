-- Additive only. Applied explicitly by --migrate-billing, never at startup.
CREATE TABLE IF NOT EXISTS guru_billing_accounts (
    enrollment_id uuid PRIMARY KEY REFERENCES guru_enrollments(id),
    vault_cipher text,
    auto_renew boolean NOT NULL DEFAULT false,
    consent_at timestamptz,
    anchor_day integer CHECK (anchor_day BETWEEN 1 AND 31),
    pending_subject_ids text[],
    mode text NOT NULL CHECK (mode IN ('sandbox','live')),
    merchant_id text
);
CREATE TABLE IF NOT EXISTS guru_billing_payments (
    id uuid PRIMARY KEY,
    enrollment_id uuid NOT NULL REFERENCES guru_enrollments(id),
    idempotency_key uuid NOT NULL UNIQUE,
    kind text NOT NULL CHECK (kind IN ('checkout','renewal','refund')),
    reference text NOT NULL UNIQUE,
    amount_cents integer NOT NULL CHECK (amount_cents IN (29000,49500)),
    subject_ids text[] NOT NULL,
    state text NOT NULL CHECK (state IN ('submitting','pending','succeeded','failed','uncertain')),
    request_id text UNIQUE,
    transaction_id text,
    original_payment_id uuid REFERENCES guru_billing_payments(id),
    period_start timestamptz,
    period_end timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    paid_at timestamptz,
    result_code text,
    retry_safe boolean NOT NULL DEFAULT false CONSTRAINT guru_billing_retry_safe_check CHECK (NOT retry_safe OR (kind='refund' AND state='failed' AND result_code IN ('11','12'))),
    mode text NOT NULL CHECK (mode IN ('sandbox','live')),
    merchant_id text,
    CHECK ((kind='refund') = (original_payment_id IS NOT NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS guru_billing_transaction_unique
    ON guru_billing_payments(transaction_id,kind,mode) WHERE transaction_id IS NOT NULL;
-- Upgrade only the newly introduced billing table/index, not any legacy domain row.
ALTER TABLE guru_billing_payments ADD COLUMN IF NOT EXISTS retry_safe boolean NOT NULL DEFAULT false;
ALTER TABLE guru_billing_accounts ADD COLUMN IF NOT EXISTS merchant_id text;
ALTER TABLE guru_billing_payments ADD COLUMN IF NOT EXISTS merchant_id text;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='guru_billing_retry_safe_check' AND conrelid='guru_billing_payments'::regclass) THEN
        ALTER TABLE guru_billing_payments ADD CONSTRAINT guru_billing_retry_safe_check
            CHECK (NOT retry_safe OR (kind='refund' AND state='failed' AND result_code IN ('11','12')));
    END IF;
END $$;
DROP INDEX IF EXISTS guru_billing_refund_unique;
CREATE UNIQUE INDEX guru_billing_refund_unique
    ON guru_billing_payments(original_payment_id) WHERE kind='refund' AND NOT retry_safe;
CREATE UNIQUE INDEX IF NOT EXISTS guru_billing_renewal_unique
    ON guru_billing_payments(enrollment_id,period_start) WHERE kind='renewal';
CREATE INDEX IF NOT EXISTS guru_billing_history
    ON guru_billing_payments(enrollment_id,created_at DESC);
CREATE INDEX IF NOT EXISTS guru_billing_unresolved
    ON guru_billing_payments(state) WHERE state IN ('submitting','pending','uncertain');
