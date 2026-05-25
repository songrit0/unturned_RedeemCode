using System;
using System.Collections.Generic;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Action = System.Action;
using Logger = Rocket.Core.Logging.Logger;

namespace RedeemCode
{
    /// <summary>
    /// Redeem-code system backed by MySQL. Players run <c>/code &lt;code&gt;</c> to claim items.
    /// Codes/items are managed directly in the database (e.g. by a Discord bot). Each code is
    /// limited to one redemption per player plus an optional global cap.
    ///
    /// DB work runs on a background thread (so the server never stalls on a query); item delivery
    /// and chat are marshalled back to the Unity main thread via <see cref="Enqueue"/>.
    /// </summary>
    public sealed class RedeemCodePlugin : RocketPlugin<RedeemCodeConfiguration>
    {
        public static RedeemCodePlugin Instance { get; private set; }
        public RedeemDatabase Database { get; private set; }

        private readonly object _lock = new object();
        private readonly Queue<Action> _mainThread = new Queue<Action>();

        protected override void Load()
        {
            Instance = this;
            DatabaseSection db = Configuration.Instance.Database;
            Database = new RedeemDatabase(db.ConnectionString, db.TablePrefix);
            Logger.Log("RedeemCode loaded.");
        }

        protected override void Unload()
        {
            lock (_lock) _mainThread.Clear();
            Database = null;
            Instance = null;
            Logger.Log("RedeemCode unloaded.");
        }

        /// <summary>Queue an action to run on the next FixedUpdate (Unity main thread).</summary>
        public void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_lock) _mainThread.Enqueue(action);
        }

        private void FixedUpdate()
        {
            while (true)
            {
                Action a = null;
                lock (_lock)
                {
                    if (_mainThread.Count > 0) a = _mainThread.Dequeue();
                }
                if (a == null) break;
                try { a(); }
                catch (Exception ex) { Logger.LogException(ex, "[RedeemCode] main-thread action"); }
            }
        }

        /// <summary>Runs on the main thread: show the result and grant items on success.</summary>
        public void DeliverResult(ulong steamId, string code, RedeemOutcome outcome)
        {
            Player player = PlayerTool.getPlayer(new CSteamID(steamId));
            if (player == null) return; // player left before the DB finished

            RedeemCodeConfiguration cfg = Configuration.Instance;
            switch (outcome.Status)
            {
                case RedeemStatus.NotFound:    Say(player, cfg.MsgNotFound, code); return;
                case RedeemStatus.Disabled:    Say(player, cfg.MsgDisabled, code); return;
                case RedeemStatus.Expired:     Say(player, cfg.MsgExpired, code); return;
                case RedeemStatus.AlreadyUsed: Say(player, cfg.MsgAlreadyUsed, code); return;
                case RedeemStatus.CapReached:  Say(player, cfg.MsgCapReached, code); return;
                case RedeemStatus.Error:       Say(player, cfg.MsgError, code); return;
                case RedeemStatus.Success:     break;
            }

            bool anyDropped = false;
            foreach (ItemReward reward in outcome.Items)
            {
                if (reward.Id == 0) continue;
                if (Assets.find(EAssetType.ITEM, reward.Id) == null)
                {
                    Logger.LogWarning("[RedeemCode] code '" + code + "' references unknown item id " + reward.Id);
                    continue;
                }
                int amount = Mathf.Clamp(reward.Amount, 1, Mathf.Max(1, cfg.MaxAmountPerItem));
                for (int i = 0; i < amount; i++)
                    if (!GiveOrDrop(player, reward.Id))
                        anyDropped = true;
            }

            Say(player, cfg.MsgSuccess, code);
            if (anyDropped)
                Say(player, cfg.MsgItemDropped, code);
        }

        /// <summary>Adds one item to the player's inventory, or drops it at their feet if full.</summary>
        private static bool GiveOrDrop(Player player, ushort itemId)
        {
            Item item = new Item(itemId, true);
            if (player.inventory.tryAddItem(item, true))
                return true;
            ItemManager.dropItem(item, player.transform.position, true, true, true);
            return false;
        }

        private static void Say(Player player, Message msg, string code)
        {
            if (player == null || msg == null || string.IsNullOrEmpty(msg.Text)) return;
            string text = msg.Text.Replace("{code}", code);
            Color color = UnturnedChat.GetColorFromName(msg.Color, Color.white);
            ChatManager.serverSendMessage(text, color, null, player.channel.owner, EChatMode.SAY, null, true);
        }
    }
}
