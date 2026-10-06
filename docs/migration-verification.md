# Architecture migration verification

Verified on 6 October 2026 in the **development workspace**, not production.

## Automated/API checks

- Both .NET projects build and publish without compiler warnings.
- The retained TypeScript source passes its existing typecheck.
- The isolated API suite passed 107 checks: verification and password recovery,
  immutable roles, grade eligibility, R290/R495 prices, duplicate subscriptions,
  pending/expired access denial, tutor ownership, attendance/progress,
  real storage upload/registration/ranged download/deletion, and single-use
  legacy invitations preserving profile IDs and parent–learner references.
- Original-row fingerprints: **9 original rows; none changed or missing**.
- Existing login cookie and parent ownership survived API restart and the
  encrypted-key upgrade. Persisted keys use encrypted envelopes.
- Missing input on four account-link/recovery routes returns HTTP 400.
- A protected downloaded MP4 matches the original bytes and has `video/mp4`
  MIME type; byte-range delivery returns HTTP 206.

## Signed-in browser journey

Using generated test accounts:

- Parent sign-in and Settings name save persisted after reload.
- Parent requested one Mathematics subject at R290, changed it to Mathematics
  plus Physical Science at R495, and saw pending/awaiting-provider status.
- Learner had no classes or active subjects while that request was pending.
- A **guarded test-only database update**, not a payment integration, enabled
  the generated enrollment to exercise paid-access paths.
- Tutor created a Mathematics course for grades 10–10, scheduled a 60-minute
  class with a Zoom HTTPS link and a private plan, edited/completed the class,
  and marked the eligible learner present.
- Tutor uploaded genuine class notes; learner downloaded them successfully.
- Tutor uploaded a genuine MP4; learner saw the protected recording.
- Learner saw the class without the private tutor plan, saw its Join link
  while scheduled, and saw Mathematics progress of two of two attended classes.
- Tutor confirmed notes deletion, and the learner no longer saw those notes.
- Grade selectors display distinct 8–12 choices. The original broken Razor
  loop capture and literal error/price bindings were corrected.

The added-second-child browser action was not re-run after fixing the selector;
parent learner creation and grade handling passed the API suite. Mobile-specific
navigation and real meeting-provider connectivity were not browser-tested.

## Codec limitation

The testing Chromium does **not** support the fixture's H.264 codec. Its
`DEMUXER_ERROR_NO_SUPPORTED_STREAMS` was a testing-browser limitation, not a
corrupt download: ffprobe validated the original one-second MP4, and the
protected download matched it byte for byte.

The same browser supported a genuine VP9-in-MP4 fixture. Uploaded through the
same tutor UI and protected storage path, it played with duration one second,
readyState 4, advancing currentTime and no error. Test normal H.264 playback in
a supporting user browser before production launch.

## Limits and cleanup

Only generated test identities, their owned rows, and their uploaded objects
are removed after verification. The original domain records, private backup,
fingerprint baseline and encrypted shared keys remain.

No production deployment, production migration, real payment, refund,
automatic meeting creation or recording import was performed. Production mail
and launch/cutover checks are separate follow-ups; payment and live-class
integrations already have their own tasks.
