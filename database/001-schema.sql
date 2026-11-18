--
-- PostgreSQL database dump
--

\restrict DZYXHqwzZBWNB98GfVStH3bCaRiKpLcNcwCj4fbs5TSbES3VxrTSjiKsVc6jmlf

-- Dumped from database version 16.10
-- Dumped by pg_dump version 16.10

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: guru_attendance; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_attendance (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    session_id uuid NOT NULL,
    learner_id uuid NOT NULL,
    attended boolean NOT NULL,
    marked_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_billing_accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_billing_accounts (
    enrollment_id uuid NOT NULL,
    vault_cipher text,
    auto_renew boolean DEFAULT false NOT NULL,
    consent_at timestamp with time zone,
    anchor_day integer,
    pending_subject_ids text[],
    mode text NOT NULL,
    merchant_id text,
    CONSTRAINT guru_billing_accounts_anchor_day_check CHECK (((anchor_day >= 1) AND (anchor_day <= 31))),
    CONSTRAINT guru_billing_accounts_mode_check CHECK ((mode = ANY (ARRAY['sandbox'::text, 'live'::text])))
);


--
-- Name: guru_billing_payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_billing_payments (
    id uuid NOT NULL,
    enrollment_id uuid NOT NULL,
    idempotency_key uuid NOT NULL,
    kind text NOT NULL,
    reference text NOT NULL,
    amount_cents integer NOT NULL,
    subject_ids text[] NOT NULL,
    state text NOT NULL,
    request_id text,
    transaction_id text,
    original_payment_id uuid,
    period_start timestamp with time zone,
    period_end timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    paid_at timestamp with time zone,
    result_code text,
    retry_safe boolean DEFAULT false NOT NULL,
    mode text NOT NULL,
    merchant_id text,
    CONSTRAINT guru_billing_payments_amount_cents_check CHECK ((amount_cents = ANY (ARRAY[29000, 49500]))),
    CONSTRAINT guru_billing_payments_check CHECK (((kind = 'refund'::text) = (original_payment_id IS NOT NULL))),
    CONSTRAINT guru_billing_payments_kind_check CHECK ((kind = ANY (ARRAY['checkout'::text, 'renewal'::text, 'refund'::text]))),
    CONSTRAINT guru_billing_payments_mode_check CHECK ((mode = ANY (ARRAY['sandbox'::text, 'live'::text]))),
    CONSTRAINT guru_billing_payments_state_check CHECK ((state = ANY (ARRAY['submitting'::text, 'pending'::text, 'succeeded'::text, 'failed'::text, 'uncertain'::text]))),
    CONSTRAINT guru_billing_retry_safe_check CHECK (((NOT retry_safe) OR ((kind = 'refund'::text) AND (state = 'failed'::text) AND (result_code = ANY (ARRAY['11'::text, '12'::text])))))
);


--
-- Name: guru_courses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_courses (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    subject_id text NOT NULL,
    title text NOT NULL,
    grade_min integer NOT NULL,
    grade_max integer NOT NULL,
    tutor_id text NOT NULL,
    schedule_summary text
);


--
-- Name: guru_enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_enrollments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    parent_id text NOT NULL,
    learner_id uuid NOT NULL,
    subject_ids text[] NOT NULL,
    monthly_amount_zar integer NOT NULL,
    status text DEFAULT 'pending'::text NOT NULL,
    payment_status text DEFAULT 'awaiting_provider'::text NOT NULL,
    period_ends_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_identity_dev_mail; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_identity_dev_mail (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    recipient text NOT NULL,
    subject text NOT NULL,
    link text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_identity_invitations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_identity_invitations (
    code_hash text NOT NULL,
    profile_id text NOT NULL,
    email text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone
);


--
-- Name: guru_identity_keys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_identity_keys (
    name text NOT NULL,
    xml text NOT NULL
);


--
-- Name: guru_identity_profile_links; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_identity_profile_links (
    identity_id text NOT NULL,
    profile_id text NOT NULL,
    linked_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_identity_users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_identity_users (
    id text NOT NULL,
    normalized_email text NOT NULL,
    normalized_username text NOT NULL,
    concurrency_stamp text NOT NULL,
    data jsonb NOT NULL
);


--
-- Name: guru_learners; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_learners (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    parent_id text NOT NULL,
    name text NOT NULL,
    email text NOT NULL,
    grade integer NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_materials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_materials (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    course_id uuid NOT NULL,
    owner_id text NOT NULL,
    title text NOT NULL,
    original_file_name text NOT NULL,
    object_path text NOT NULL,
    content_type text NOT NULL,
    size_bytes integer NOT NULL,
    uploaded_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_profiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_profiles (
    id text NOT NULL,
    full_name text NOT NULL,
    email text NOT NULL,
    role text NOT NULL,
    phone text,
    grade integer,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_recordings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_recordings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    session_id uuid NOT NULL,
    owner_id text NOT NULL,
    title text NOT NULL,
    object_path text NOT NULL,
    duration_seconds integer NOT NULL,
    uploaded_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_sessions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    course_id uuid NOT NULL,
    title text NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    duration_minutes integer DEFAULT 60 NOT NULL,
    status text DEFAULT 'scheduled'::text NOT NULL,
    meeting_url text,
    lesson_plan text
);


--
-- Name: guru_subjects; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_subjects (
    id text NOT NULL,
    name text NOT NULL,
    grades integer[] NOT NULL,
    description text NOT NULL,
    price_monthly_zar integer DEFAULT 290 NOT NULL
);


--
-- Name: guru_uploads; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.guru_uploads (
    object_path text NOT NULL,
    owner_id text NOT NULL,
    name text NOT NULL,
    size integer NOT NULL,
    content_type text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: guru_attendance guru_attendance_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_attendance
    ADD CONSTRAINT guru_attendance_pkey PRIMARY KEY (id);


--
-- Name: guru_billing_accounts guru_billing_accounts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_accounts
    ADD CONSTRAINT guru_billing_accounts_pkey PRIMARY KEY (enrollment_id);


--
-- Name: guru_billing_payments guru_billing_payments_idempotency_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_payments
    ADD CONSTRAINT guru_billing_payments_idempotency_key_key UNIQUE (idempotency_key);


--
-- Name: guru_billing_payments guru_billing_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_payments
    ADD CONSTRAINT guru_billing_payments_pkey PRIMARY KEY (id);


--
-- Name: guru_billing_payments guru_billing_payments_reference_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_payments
    ADD CONSTRAINT guru_billing_payments_reference_key UNIQUE (reference);


--
-- Name: guru_billing_payments guru_billing_payments_request_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_payments
    ADD CONSTRAINT guru_billing_payments_request_id_key UNIQUE (request_id);


--
-- Name: guru_courses guru_courses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_courses
    ADD CONSTRAINT guru_courses_pkey PRIMARY KEY (id);


--
-- Name: guru_enrollments guru_enrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_enrollments
    ADD CONSTRAINT guru_enrollments_pkey PRIMARY KEY (id);


--
-- Name: guru_identity_dev_mail guru_identity_dev_mail_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_dev_mail
    ADD CONSTRAINT guru_identity_dev_mail_pkey PRIMARY KEY (id);


--
-- Name: guru_identity_invitations guru_identity_invitations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_invitations
    ADD CONSTRAINT guru_identity_invitations_pkey PRIMARY KEY (code_hash);


--
-- Name: guru_identity_keys guru_identity_keys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_keys
    ADD CONSTRAINT guru_identity_keys_pkey PRIMARY KEY (name);


--
-- Name: guru_identity_profile_links guru_identity_profile_links_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_profile_links
    ADD CONSTRAINT guru_identity_profile_links_pkey PRIMARY KEY (identity_id);


--
-- Name: guru_identity_profile_links guru_identity_profile_links_profile_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_profile_links
    ADD CONSTRAINT guru_identity_profile_links_profile_id_key UNIQUE (profile_id);


--
-- Name: guru_identity_users guru_identity_users_normalized_email_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_users
    ADD CONSTRAINT guru_identity_users_normalized_email_key UNIQUE (normalized_email);


--
-- Name: guru_identity_users guru_identity_users_normalized_username_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_users
    ADD CONSTRAINT guru_identity_users_normalized_username_key UNIQUE (normalized_username);


--
-- Name: guru_identity_users guru_identity_users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_users
    ADD CONSTRAINT guru_identity_users_pkey PRIMARY KEY (id);


--
-- Name: guru_learners guru_learners_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_learners
    ADD CONSTRAINT guru_learners_pkey PRIMARY KEY (id);


--
-- Name: guru_materials guru_materials_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_materials
    ADD CONSTRAINT guru_materials_pkey PRIMARY KEY (id);


--
-- Name: guru_profiles guru_profiles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_profiles
    ADD CONSTRAINT guru_profiles_pkey PRIMARY KEY (id);


--
-- Name: guru_recordings guru_recordings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_recordings
    ADD CONSTRAINT guru_recordings_pkey PRIMARY KEY (id);


--
-- Name: guru_sessions guru_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_sessions
    ADD CONSTRAINT guru_sessions_pkey PRIMARY KEY (id);


--
-- Name: guru_subjects guru_subjects_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_subjects
    ADD CONSTRAINT guru_subjects_pkey PRIMARY KEY (id);


--
-- Name: guru_uploads guru_uploads_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_uploads
    ADD CONSTRAINT guru_uploads_pkey PRIMARY KEY (object_path);


--
-- Name: guru_attendance_session_learner; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX guru_attendance_session_learner ON public.guru_attendance USING btree (session_id, learner_id);


--
-- Name: guru_billing_history; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX guru_billing_history ON public.guru_billing_payments USING btree (enrollment_id, created_at DESC);


--
-- Name: guru_billing_refund_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX guru_billing_refund_unique ON public.guru_billing_payments USING btree (original_payment_id) WHERE ((kind = 'refund'::text) AND (NOT retry_safe));


--
-- Name: guru_billing_renewal_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX guru_billing_renewal_unique ON public.guru_billing_payments USING btree (enrollment_id, period_start) WHERE (kind = 'renewal'::text);


--
-- Name: guru_billing_transaction_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX guru_billing_transaction_unique ON public.guru_billing_payments USING btree (transaction_id, kind, mode) WHERE (transaction_id IS NOT NULL);


--
-- Name: guru_billing_unresolved; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX guru_billing_unresolved ON public.guru_billing_payments USING btree (state) WHERE (state = ANY (ARRAY['submitting'::text, 'pending'::text, 'uncertain'::text]));


--
-- Name: guru_enrollment_current_learner; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX guru_enrollment_current_learner ON public.guru_enrollments USING btree (learner_id) WHERE (status <> 'cancelled'::text);


--
-- Name: guru_learner_parent_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX guru_learner_parent_email ON public.guru_learners USING btree (parent_id, email);


--
-- Name: guru_attendance guru_attendance_learner_id_guru_learners_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_attendance
    ADD CONSTRAINT guru_attendance_learner_id_guru_learners_id_fk FOREIGN KEY (learner_id) REFERENCES public.guru_learners(id);


--
-- Name: guru_attendance guru_attendance_session_id_guru_sessions_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_attendance
    ADD CONSTRAINT guru_attendance_session_id_guru_sessions_id_fk FOREIGN KEY (session_id) REFERENCES public.guru_sessions(id);


--
-- Name: guru_billing_accounts guru_billing_accounts_enrollment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_accounts
    ADD CONSTRAINT guru_billing_accounts_enrollment_id_fkey FOREIGN KEY (enrollment_id) REFERENCES public.guru_enrollments(id);


--
-- Name: guru_billing_payments guru_billing_payments_enrollment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_payments
    ADD CONSTRAINT guru_billing_payments_enrollment_id_fkey FOREIGN KEY (enrollment_id) REFERENCES public.guru_enrollments(id);


--
-- Name: guru_billing_payments guru_billing_payments_original_payment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_billing_payments
    ADD CONSTRAINT guru_billing_payments_original_payment_id_fkey FOREIGN KEY (original_payment_id) REFERENCES public.guru_billing_payments(id);


--
-- Name: guru_courses guru_courses_subject_id_guru_subjects_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_courses
    ADD CONSTRAINT guru_courses_subject_id_guru_subjects_id_fk FOREIGN KEY (subject_id) REFERENCES public.guru_subjects(id);


--
-- Name: guru_courses guru_courses_tutor_id_guru_profiles_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_courses
    ADD CONSTRAINT guru_courses_tutor_id_guru_profiles_id_fk FOREIGN KEY (tutor_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_enrollments guru_enrollments_learner_id_guru_learners_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_enrollments
    ADD CONSTRAINT guru_enrollments_learner_id_guru_learners_id_fk FOREIGN KEY (learner_id) REFERENCES public.guru_learners(id);


--
-- Name: guru_enrollments guru_enrollments_parent_id_guru_profiles_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_enrollments
    ADD CONSTRAINT guru_enrollments_parent_id_guru_profiles_id_fk FOREIGN KEY (parent_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_identity_invitations guru_identity_invitations_profile_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_invitations
    ADD CONSTRAINT guru_identity_invitations_profile_id_fkey FOREIGN KEY (profile_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_identity_profile_links guru_identity_profile_links_identity_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_profile_links
    ADD CONSTRAINT guru_identity_profile_links_identity_id_fkey FOREIGN KEY (identity_id) REFERENCES public.guru_identity_users(id);


--
-- Name: guru_identity_profile_links guru_identity_profile_links_profile_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_identity_profile_links
    ADD CONSTRAINT guru_identity_profile_links_profile_id_fkey FOREIGN KEY (profile_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_learners guru_learners_parent_id_guru_profiles_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_learners
    ADD CONSTRAINT guru_learners_parent_id_guru_profiles_id_fk FOREIGN KEY (parent_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_materials guru_materials_course_id_guru_courses_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_materials
    ADD CONSTRAINT guru_materials_course_id_guru_courses_id_fk FOREIGN KEY (course_id) REFERENCES public.guru_courses(id);


--
-- Name: guru_materials guru_materials_owner_id_guru_profiles_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_materials
    ADD CONSTRAINT guru_materials_owner_id_guru_profiles_id_fk FOREIGN KEY (owner_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_recordings guru_recordings_owner_id_guru_profiles_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_recordings
    ADD CONSTRAINT guru_recordings_owner_id_guru_profiles_id_fk FOREIGN KEY (owner_id) REFERENCES public.guru_profiles(id);


--
-- Name: guru_recordings guru_recordings_session_id_guru_sessions_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_recordings
    ADD CONSTRAINT guru_recordings_session_id_guru_sessions_id_fk FOREIGN KEY (session_id) REFERENCES public.guru_sessions(id);


--
-- Name: guru_sessions guru_sessions_course_id_guru_courses_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_sessions
    ADD CONSTRAINT guru_sessions_course_id_guru_courses_id_fk FOREIGN KEY (course_id) REFERENCES public.guru_courses(id);


--
-- Name: guru_uploads guru_uploads_owner_id_guru_profiles_id_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.guru_uploads
    ADD CONSTRAINT guru_uploads_owner_id_guru_profiles_id_fk FOREIGN KEY (owner_id) REFERENCES public.guru_profiles(id);


--
-- PostgreSQL database dump complete
--

\unrestrict DZYXHqwzZBWNB98GfVStH3bCaRiKpLcNcwCj4fbs5TSbES3VxrTSjiKsVc6jmlf

