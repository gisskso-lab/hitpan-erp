ALTER TABLE tenants
    ADD COLUMN serial_verified TINYINT(1) NOT NULL DEFAULT 0 AFTER status,
    ADD COLUMN serial_verified_at DATETIME NULL AFTER serial_verified,
    ADD COLUMN serial_verified_fingerprint VARCHAR(128) NULL AFTER serial_verified_at;
SELECT 'serial_cols_added' AS status;
DESCRIBE tenants;
