-- 001_state_columns.sql
-- Adds quality / state / rot columns to rc_code_items so codes can carry full
-- item state (attachments, ammo, durability) for the P2P marketplace flow.
--
-- The plugin's EnsureSchema() runs the equivalent check-then-ALTER on load via
-- INFORMATION_SCHEMA, so applying this script manually is optional. Provided
-- here as documentation and a fallback for ops who prefer explicit migrations.
--
-- Safe to re-run: the INFORMATION_SCHEMA guard means existing columns are
-- skipped without touching data. Adjust the table prefix if not using `rc_`.

SET @prefix := 'rc_';
SET @table  := CONCAT(@prefix, 'code_items');

-- quality
SET @sql := IF(
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = 'quality') = 0,
    CONCAT('ALTER TABLE `', @table, '` ADD COLUMN `quality` TINYINT UNSIGNED NOT NULL DEFAULT 100 AFTER `amount`;'),
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- state
SET @sql := IF(
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = 'state') = 0,
    CONCAT('ALTER TABLE `', @table, '` ADD COLUMN `state` LONGTEXT NULL AFTER `quality`;'),
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- rot
SET @sql := IF(
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = 'rot') = 0,
    CONCAT('ALTER TABLE `', @table, '` ADD COLUMN `rot` TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER `state`;'),
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
