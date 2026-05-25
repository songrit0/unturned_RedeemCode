using System;
using System.Collections.Generic;
using System.Threading;
using Rocket.API;
using Rocket.Unturned.Player;

namespace RedeemCode
{
    /// <summary>
    /// <c>/code &lt;code&gt;</c> - redeem a code for items. The DB lookup runs on a background
    /// thread; the result + items are delivered on the main thread. Permission: <c>redeemcode.use</c>.
    /// </summary>
    public sealed class CommandCode : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "code";
        public string Help => "Redeem a code for items.";
        public string Syntax => "<code>";
        public List<string> Aliases => new List<string> { "redeem" };
        public List<string> Permissions => new List<string> { "redeemcode.use" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            RedeemCodePlugin plugin = RedeemCodePlugin.Instance;
            if (plugin?.Database == null) return;

            UnturnedPlayer up = caller as UnturnedPlayer;
            if (up?.Player == null) return;

            if (command.Length < 1 || string.IsNullOrEmpty(command[0]))
            {
                Rocket.Unturned.Chat.UnturnedChat.Say(caller,
                    plugin.Configuration.Instance.MsgUsage.Text,
                    UnityEngine.Color.yellow);
                return;
            }

            string code = command[0].Trim();
            ulong steamId = up.CSteamID.m_SteamID;

            // Offload the DB round-trip; deliver back on the main thread.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                RedeemDatabase db = plugin.Database;
                if (db == null) return;

                RedeemOutcome outcome;
                try { outcome = db.Redeem(steamId, code); }
                catch (Exception)
                {
                    outcome = new RedeemOutcome { Status = RedeemStatus.Error };
                }

                RedeemCodePlugin live = RedeemCodePlugin.Instance;
                live?.Enqueue(() => live.DeliverResult(steamId, code, outcome));
            });
        }
    }
}
