using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;

namespace ToolCupboard
{
    /// <summary>
    /// <c>/decay</c> - tells the caller whether the spot they are standing on is protected,
    /// and by what kind of device. Replaces the original plugin's on-screen UI.
    /// Permission node: <c>toolcupboard.decay</c>.
    /// </summary>
    public sealed class CommandDecay : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "decay";
        public string Help => "Check whether your current position is protected from decay.";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "toolcupboard.decay" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            ToolCupboardPlugin plugin = ToolCupboardPlugin.Instance;
            if (plugin?.Engine == null)
                return;

            UnturnedPlayer up = caller as UnturnedPlayer;
            if (up?.Player == null)
                return;

            Player player = up.Player;
            Vector3 pos = player.transform.position;
            ulong owner = up.CSteamID.m_SteamID;
            ulong group = player.quests.groupID.m_SteamID;

            ToolCupboardConfiguration cfg = plugin.Configuration.Instance;

            if (plugin.Engine.QueryProtection(pos, owner, group, out EProtectionType type))
            {
                ChatNotifier.Send(player, cfg.MsgStatusProtected, type: TypeName(type));
            }
            else
            {
                ChatNotifier.Send(player, cfg.MsgStatusUnprotected, how: ChatNotifier.BuildHowToProtect(cfg));
                int secs = Mathf.RoundToInt(plugin.Engine.SecondsUntilDecay);
                UnturnedChat.Say(caller, "Next decay pass in ~" + secs + "s | รอบผุถัดไปอีก ~" + secs + " วิ", Color.gray);
            }

            ShowProtectionRings(plugin, cfg, pos, owner, group);
        }

        /// <summary>Draws a radius ring around each of the caller's own/group protection devices nearby.</summary>
        private static void ShowProtectionRings(ToolCupboardPlugin plugin, ToolCupboardConfiguration cfg, Vector3 pos, ulong owner, ulong group)
        {
            if (!cfg.Visual.ShowProtectionRings || plugin.Rings == null)
                return;

            List<ProtectionSource> sources = plugin.Engine.GetOwnedSourcesNear(pos, owner, group, cfg.Visual.RingDisplayRange);
            if (sources.Count == 0)
                return;

            List<KeyValuePair<Vector3, float>> rings = new List<KeyValuePair<Vector3, float>>(sources.Count);
            foreach (ProtectionSource s in sources)
                rings.Add(new KeyValuePair<Vector3, float>(s.Center, Mathf.Sqrt(s.RadiusSqr)));

            plugin.Rings.Show(owner, rings);
        }

        private static string TypeName(EProtectionType type)
        {
            switch (type)
            {
                case EProtectionType.ClaimFlag: return "Claim Flag";
                case EProtectionType.Generator: return "Generator";
                case EProtectionType.Bed: return "Bed";
                default: return "Custom Item";
            }
        }
    }
}
