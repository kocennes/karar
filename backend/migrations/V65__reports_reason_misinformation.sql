-- Allow 'misinformation' ("Yanlış Bilgi") as a report reason.
-- V1 created reports.reason with a column-level CHECK that Postgres auto-named reports_reason_check
-- (same convention V58 relied on for reports_status_check). POST /api/v1/reports and the Flutter
-- report sheet already accept 'misinformation', so the INSERT violated the CHECK and returned 500.
-- Replaces the old CHECK constraint with one matching the backend allowlist. Idempotent.
--
-- Order-independent: MigrationsRunner applies every unapplied file, so a database that already
-- got a later migration widening this CHECK (e.g. V95 adding 'minor') may run V65 afterwards.
-- If the constraint already allows 'misinformation', a later list is in place: leave it alone.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'reports'::regclass
          AND conname = 'reports_reason_check'
          AND pg_get_constraintdef(oid) LIKE '%''misinformation''%'
    ) THEN
        RETURN;
    END IF;

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
                'other'
            ));
END
$$;
