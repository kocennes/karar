ALTER TABLE reports ADD COLUMN IF NOT EXISTS reporter_ip_block TEXT;

CREATE INDEX IF NOT EXISTS idx_reports_target_ip_block
    ON reports(target_type, target_id, reporter_ip_block)
    WHERE status = 'pending';
