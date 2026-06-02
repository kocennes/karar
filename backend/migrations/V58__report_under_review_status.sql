-- Allow 'under_review' as a report status so moderators can signal active review.
-- Replaces the old CHECK constraint with one that includes the new value.
ALTER TABLE reports
    DROP CONSTRAINT IF EXISTS reports_status_check;

ALTER TABLE reports
    ADD CONSTRAINT reports_status_check
        CHECK (status IN ('pending', 'under_review', 'actioned', 'dismissed'));
