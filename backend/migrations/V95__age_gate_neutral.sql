-- P0-02: temporary neutral 18+ age gate (until the e-Devlet age token, P0-01).
--
-- 1) Device-level lock. A device that declared an ineligible birth date keeps the gate closed:
--    /auth/register, /users/me/onboarding and /devices/age-declaration refuse it from then on.
--    Only the timestamp is stored, never the declared birth date.
-- 2) Guest age declaration (used only when AgeGate:GuestVote is on): the timestamp of the
--    device's neutral declaration before its first guest vote. Again no birth date is stored.
-- Writes use COALESCE(..., NOW()) so repeated calls keep the first timestamp (idempotent).
ALTER TABLE devices
    ADD COLUMN IF NOT EXISTS age_gate_failed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS age_declared_at    TIMESTAMPTZ;

-- 3) New report reason 'minor' ("Reşit olmayan kullanıcı").
-- V1 created reports.reason with a column-level CHECK that Postgres auto-named reports_reason_check
-- (V58 and V65 rely on the same convention). This list is a superset of V1 and of V65
-- (PR #19, 'misinformation') and equals the POST /api/v1/reports allowlist. On a fresh
-- database V65 runs first and V95 widens it. A database that gets V95 before V65 would be
-- narrowed again by V65 unless V65 carries the same list, so V65 should include 'minor' too.
ALTER TABLE reports
    DROP CONSTRAINT IF EXISTS reports_reason_check;

ALTER TABLE reports
    ADD CONSTRAINT reports_reason_check
        CHECK (reason IN (
            'hate_speech',
            'harassment',
            'personal_info',
            'misinformation',
            'spam',
            'self_harm',
            'illegal',
            'minor',
            'other'
        ));
