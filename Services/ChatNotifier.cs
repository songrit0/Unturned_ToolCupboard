using System.Collections.Generic;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace ToolCupboard
{
    /// <summary>
    /// Sends decay feedback to players through chat (this plugin has no UI).
    /// Per-owner cooldowns keep a player from being spammed when many of their
    /// buildables decay in the same pass.
    /// </summary>
    public sealed class ChatNotifier
    {
        private readonly ToolCupboardConfiguration _cfg;
        private readonly Dictionary<ulong, float> _lastWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastDestroyed = new Dictionary<ulong, float>();

        public ChatNotifier(ToolCupboardConfiguration cfg)
        {
            _cfg = cfg;
        }

        private static float Now => Time.realtimeSinceStartup;

        private bool Ready(Dictionary<ulong, float> seen, ulong owner)
        {
            return !seen.TryGetValue(owner, out float last) || Now - last >= _cfg.WarnCooldown;
        }

        /// <summary>Warn an online owner that <paramref name="count"/> of their parts are decaying.</summary>
        public void WarnDecaying(ulong owner, int count)
        {
            if (owner == 0 || count <= 0 || !Ready(_lastWarn, owner))
                return;
            Player player = PlayerTool.getPlayer(new CSteamID(owner));
            if (player == null)
                return; // offline - nothing to deliver, no state kept
            Send(player, _cfg.MsgDecaying, count, how: BuildHowToProtect(_cfg));
            _lastWarn[owner] = Now;
        }

        /// <summary>Tell an online owner that decay destroyed <paramref name="count"/> of their parts.</summary>
        public void NotifyDestroyed(ulong owner, int count)
        {
            if (owner == 0 || count <= 0 || !Ready(_lastDestroyed, owner))
                return;
            Player player = PlayerTool.getPlayer(new CSteamID(owner));
            if (player == null)
                return;
            Send(player, _cfg.MsgDestroyed, count, how: BuildHowToProtect(_cfg));
            _lastDestroyed[owner] = Now;
        }

        /// <summary>
        /// Builds the "how to protect" hint shown via the {how} placeholder. Lists only the
        /// protection methods that are currently enabled, with their radii, so it always matches
        /// the live config.
        /// </summary>
        public static string BuildHowToProtect(ToolCupboardConfiguration cfg)
        {
            ProtectionSettings p = cfg.Protection;
            List<string> parts = new List<string>();
            if (p.UseClaimFlags)
                parts.Add("place a Claim Flag within " + (int)p.ClaimFlagRadius + "m / ปัก Claim Flag ในระยะ " + (int)p.ClaimFlagRadius + "m");
            if (p.UseGenerators)
                parts.Add("turn on a Generator within " + (int)p.GeneratorRadius + "m / เปิดเครื่องปั่นไฟในระยะ " + (int)p.GeneratorRadius + "m");
            if (p.UseBeds)
                parts.Add("place a claimed Bed within " + (int)p.BedRadius + "m / วางเตียงที่ claim ในระยะ " + (int)p.BedRadius + "m");
            if (p.CustomItems != null && p.CustomItems.Length > 0)
                parts.Add("place a configured item / วางไอเทมป้องกันที่กำหนด");
            return parts.Count == 0 ? "(no protection enabled / ยังไม่ได้เปิดวิธีป้องกัน)" : string.Join(" , ", parts.ToArray());
        }

        public void Clear()
        {
            _lastWarn.Clear();
            _lastDestroyed.Clear();
        }

        /// <summary>Sends a configured message to one player, substituting {count}, {type} and {how}.</summary>
        public static void Send(Player player, Message msg, int count = 0, string type = null, string how = null)
        {
            if (player == null || msg == null || string.IsNullOrEmpty(msg.Text))
                return;

            string text = msg.Text;
            if (text.IndexOf("{count}") >= 0)
                text = text.Replace("{count}", count.ToString());
            if (type != null && text.IndexOf("{type}") >= 0)
                text = text.Replace("{type}", type);
            if (how != null && text.IndexOf("{how}") >= 0)
                text = text.Replace("{how}", how);

            Color color = UnturnedChat.GetColorFromName(msg.Color, Color.white);
            ChatManager.serverSendMessage(
                text, color,
                fromPlayer: null,
                toPlayer: player.channel.owner,
                mode: EChatMode.SAY,
                iconURL: null,
                useRichTextFormatting: true);
        }
    }
}
