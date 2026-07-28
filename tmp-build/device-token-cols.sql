ALTER TABLE tenant_devices
    ADD COLUMN device_token_hash VARCHAR(128) NULL AFTER fingerprint,
    ADD COLUMN issued_at DATETIME NULL AFTER device_token_hash,
    ADD COLUMN os_info VARCHAR(200) NULL AFTER user_agent,
    ADD INDEX ix_device_token_hash (device_token_hash);

SELECT 'device_token_cols_added' AS status;
DESCRIBE tenant_devices;
