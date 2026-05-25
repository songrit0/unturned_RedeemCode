using System;
using System.Collections.Generic;
using System.Text;
using MySql.Data.MySqlClient;
using Logger = Rocket.Core.Logging.Logger;

namespace RedeemCode
{
    public enum RedeemStatus
    {
        Success,
        NotFound,
        Disabled,
        Expired,
        AlreadyUsed,
        CapReached,
        Error
    }

    public struct ItemReward
    {
        public ushort Id;
        public int Amount;
    }

    public sealed class RedeemOutcome
    {
        public RedeemStatus Status;
        public List<ItemReward> Items = new List<ItemReward>();
    }

    /// <summary>
    /// MySQL storage for redeem codes. Connections are opened per call (matching the other
    /// plugins on this server). All methods here run on a background thread; never touch the
    /// Unturned API from inside this class.
    ///
    /// Tables (prefix configurable):
    ///   &lt;p&gt;codes        (id, code UNIQUE, max_uses, uses, enabled, expires_at, created_at)
    ///   &lt;p&gt;code_items   (id, code_id, item_id, amount)
    ///   &lt;p&gt;redemptions  (id, code_id, steam_id, redeemed_at, UNIQUE(code_id, steam_id))
    /// </summary>
    public sealed class RedeemDatabase
    {
        private readonly string _conn;
        private readonly string _codes;
        private readonly string _items;
        private readonly string _redemptions;

        public RedeemDatabase(string connectionString, string tablePrefix)
        {
            _conn = connectionString;
            string p = SanitizePrefix(tablePrefix);
            _codes = p + "codes";
            _items = p + "code_items";
            _redemptions = p + "redemptions";
            EnsureSchema();
        }

        private static string SanitizePrefix(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "rc_";
            StringBuilder sb = new StringBuilder();
            foreach (char c in raw)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            return sb.Length == 0 ? "rc_" : sb.ToString();
        }

        private void EnsureSchema()
        {
            try
            {
                using (MySqlConnection c = new MySqlConnection(_conn))
                {
                    c.Open();
                    Exec(c, "CREATE TABLE IF NOT EXISTS `" + _codes + "` ("
                        + "`id` INT AUTO_INCREMENT PRIMARY KEY,"
                        + "`code` VARCHAR(64) NOT NULL UNIQUE,"
                        + "`max_uses` INT NOT NULL DEFAULT 0,"      // 0 = unlimited
                        + "`uses` INT NOT NULL DEFAULT 0,"
                        + "`enabled` TINYINT(1) NOT NULL DEFAULT 1,"
                        + "`expires_at` DATETIME NULL,"
                        + "`created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP"
                        + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

                    Exec(c, "CREATE TABLE IF NOT EXISTS `" + _items + "` ("
                        + "`id` INT AUTO_INCREMENT PRIMARY KEY,"
                        + "`code_id` INT NOT NULL,"
                        + "`item_id` INT UNSIGNED NOT NULL,"
                        + "`amount` INT UNSIGNED NOT NULL DEFAULT 1,"
                        + "INDEX `idx_code` (`code_id`)"
                        + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

                    Exec(c, "CREATE TABLE IF NOT EXISTS `" + _redemptions + "` ("
                        + "`id` INT AUTO_INCREMENT PRIMARY KEY,"
                        + "`code_id` INT NOT NULL,"
                        + "`steam_id` BIGINT UNSIGNED NOT NULL,"
                        + "`redeemed_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,"
                        + "UNIQUE KEY `uq_code_player` (`code_id`, `steam_id`),"
                        + "INDEX `idx_steam` (`steam_id`)"
                        + ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
                }
                Logger.Log("[RedeemCode] MySQL tables ready (" + _codes + ", " + _items + ", " + _redemptions + ").");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[RedeemCode] EnsureSchema failed");
            }
        }

        private static void Exec(MySqlConnection c, string sql)
        {
            using (MySqlCommand cmd = new MySqlCommand(sql, c)) cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Validates and (on success) atomically reserves one use of the code for this player,
        /// then returns the items to grant. Race-safe via the redemptions unique key and a
        /// conditional uses update.
        /// </summary>
        public RedeemOutcome Redeem(ulong steamId, string code)
        {
            RedeemOutcome outcome = new RedeemOutcome();
            try
            {
                using (MySqlConnection c = new MySqlConnection(_conn))
                {
                    c.Open();

                    int codeId;
                    int maxUses;
                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT `id`,`max_uses`,`uses`,`enabled`,`expires_at` FROM `" + _codes + "` WHERE `code`=@code LIMIT 1;", c))
                    {
                        cmd.Parameters.AddWithValue("@code", code);
                        using (MySqlDataReader r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) { outcome.Status = RedeemStatus.NotFound; return outcome; }

                            codeId = Convert.ToInt32(r["id"]);
                            maxUses = Convert.ToInt32(r["max_uses"]);
                            int uses = Convert.ToInt32(r["uses"]);
                            bool enabled = Convert.ToInt32(r["enabled"]) != 0;
                            object exp = r["expires_at"];

                            if (!enabled) { outcome.Status = RedeemStatus.Disabled; return outcome; }
                            if (exp != null && exp != DBNull.Value && Convert.ToDateTime(exp) < DateTime.Now)
                            { outcome.Status = RedeemStatus.Expired; return outcome; }
                            if (maxUses > 0 && uses >= maxUses) { outcome.Status = RedeemStatus.CapReached; return outcome; }
                        }
                    }

                    // Already redeemed by this player?
                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT 1 FROM `" + _redemptions + "` WHERE `code_id`=@id AND `steam_id`=@s LIMIT 1;", c))
                    {
                        cmd.Parameters.AddWithValue("@id", codeId);
                        cmd.Parameters.AddWithValue("@s", steamId);
                        if (cmd.ExecuteScalar() != null) { outcome.Status = RedeemStatus.AlreadyUsed; return outcome; }
                    }

                    // Reserve atomically: record redemption + bump uses under the cap.
                    using (MySqlTransaction tx = c.BeginTransaction())
                    {
                        try
                        {
                            using (MySqlCommand cmd = new MySqlCommand(
                                "INSERT INTO `" + _redemptions + "` (`code_id`,`steam_id`) VALUES (@id,@s);", c, tx))
                            {
                                cmd.Parameters.AddWithValue("@id", codeId);
                                cmd.Parameters.AddWithValue("@s", steamId);
                                cmd.ExecuteNonQuery();
                            }

                            using (MySqlCommand cmd = new MySqlCommand(
                                "UPDATE `" + _codes + "` SET `uses`=`uses`+1 WHERE `id`=@id AND (`max_uses`=0 OR `uses`<`max_uses`);", c, tx))
                            {
                                cmd.Parameters.AddWithValue("@id", codeId);
                                if (cmd.ExecuteNonQuery() == 0)
                                {
                                    tx.Rollback();
                                    outcome.Status = RedeemStatus.CapReached;
                                    return outcome;
                                }
                            }

                            tx.Commit();
                        }
                        catch (MySqlException dup) when (dup.Number == 1062) // duplicate key -> raced double redeem
                        {
                            try { tx.Rollback(); } catch { }
                            outcome.Status = RedeemStatus.AlreadyUsed;
                            return outcome;
                        }
                    }

                    // Load the rewards.
                    using (MySqlCommand cmd = new MySqlCommand(
                        "SELECT `item_id`,`amount` FROM `" + _items + "` WHERE `code_id`=@id;", c))
                    {
                        cmd.Parameters.AddWithValue("@id", codeId);
                        using (MySqlDataReader r = cmd.ExecuteReader())
                            while (r.Read())
                                outcome.Items.Add(new ItemReward
                                {
                                    Id = (ushort)Convert.ToUInt32(r["item_id"]),
                                    Amount = Convert.ToInt32(r["amount"])
                                });
                    }

                    outcome.Status = RedeemStatus.Success;
                    return outcome;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[RedeemCode] Redeem failed for code '" + code + "'");
                outcome.Status = RedeemStatus.Error;
                return outcome;
            }
        }
    }
}
