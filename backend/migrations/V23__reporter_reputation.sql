-- V23: Reporter reputation — track report accuracy per user
-- reporter_user_id links a report to the authenticated user who filed it (NULL for anonymous reporters).
-- reporter_accurate_count / reporter_total_count feed into a Bayesian weight [0.5, 2.0]
-- that scales how much each reporter's report counts toward the auto-hide threshold.

ALTER TABLE reports ADD COLUMN reporter_user_id UUID REFERENCES users(id) ON DELETE SET NULL;
CREATE INDEX idx_reports_reporter_user_id ON reports(reporter_user_id) WHERE reporter_user_id IS NOT NULL;

ALTER TABLE users ADD COLUMN reporter_accurate_count INT NOT NULL DEFAULT 0;
ALTER TABLE users ADD COLUMN reporter_total_count   INT NOT NULL DEFAULT 0;
