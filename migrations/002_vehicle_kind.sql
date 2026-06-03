-- 002_vehicle_kind.sql
-- Adds a `kind` column to rc_code_items so a reward row can grant a VEHICLE
-- instead of an item. Unturned vehicle ids and item ids live in separate id
-- spaces, so `kind` selects which space `item_id` refers to:
--   0 = item    (item_id is an item id)        <-- default, existing behaviour
--   1 = vehicle (item_id is a vehicle id)
--
-- The plugin's EnsureSchema() runs the equivalent check-then-ALTER on load via
-- INFORMATION_SCHEMA, so applying this script manually is optional. Provided
-- here as documentation and a fallback for ops who prefer explicit migrations.
--
-- Safe to re-run: the INFORMATION_SCHEMA guard means an existing column is
-- skipped without touching data. Adjust the table prefix if not using `rc_`.

SET @prefix := 'rc_';
SET @table  := CONCAT(@prefix, 'code_items');

-- kind
SET @sql := IF(
    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = 'kind') = 0,
    CONCAT('ALTER TABLE `', @table, '` ADD COLUMN `kind` TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER `rot`;'),
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
