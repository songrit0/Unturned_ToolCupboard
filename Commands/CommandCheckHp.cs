using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;

namespace ToolCupboard
{
    /// <summary>
    /// <c>/checkhp</c> (aliases <c>/hp</c>, <c>/check</c>) - raycasts from where the caller is looking
    /// and reports the HP of the barricade/structure they are aiming at, plus its protection status
    /// (protected / unprotected / invincible). Chat-only, bilingual EN/TH like the rest of the plugin.
    /// Permission node: <c>toolcupboard.checkhp</c>.
    /// </summary>
    public sealed class CommandCheckHp : IRocketCommand
    {
        /// <summary>How far (metres) the look ray reaches for a buildable.</summary>
        private const float Range = 8f;

        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "checkhp";
        public string Help => "Show the HP of the barricade/structure you are looking at.";
        public string Syntax => "";
        public List<string> Aliases => new List<string> { "hp", "check" };
        public List<string> Permissions => new List<string> { "toolcupboard.checkhp" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            ToolCupboardPlugin plugin = ToolCupboardPlugin.Instance;
            if (plugin?.Engine == null)
                return;

            UnturnedPlayer up = caller as UnturnedPlayer;
            if (up?.Player == null)
                return;

            Player player = up.Player;
            PlayerLook look = player.look;

            RaycastInfo info = DamageTool.raycast(
                new Ray(look.aim.position, look.aim.forward),
                Range,
                RayMasks.BARRICADE | RayMasks.STRUCTURE,
                player);

            Transform t = info.transform;
            if (t == null)
            {
                Tell(player, "Aim at a barricade or structure first | เล็งไปที่สิ่งก่อสร้างก่อน", Color.gray);
                return;
            }

            ushort cur, max;
            ulong owner, group;
            Vector3 point;
            string name;
            if (!Resolve(t, out cur, out max, out owner, out group, out point, out name) || max == 0)
            {
                Tell(player, "Aim at a barricade or structure first | เล็งไปที่สิ่งก่อสร้างก่อน", Color.gray);
                return;
            }

            int pct = Mathf.Clamp(Mathf.RoundToInt(100f * cur / max), 0, 100);

            // QueryProtection rebuilds the source snapshot, so the IsInvincible read right after is fresh.
            bool protectedSpot = plugin.Engine.QueryProtection(point, owner, group, out EProtectionType type);
            bool invincible = plugin.Engine.IsInvincible(point, owner, group);

            string status = invincible
                ? "<color=#66ccff>INVINCIBLE - never breaks | กันทุกอย่าง พังไม่ได้</color>"
                : protectedSpot
                    ? "<color=#66ff66>Protected by " + TypeName(type) + " | ป้องกันโดย " + TypeName(type) + "</color>"
                    : "<color=#ffcc44>Unprotected - will decay | ไม่ป้องกัน จะผุ</color>";

            Tell(player,
                name + ": <b>" + cur + "/" + max + "</b> HP (" + pct + "%)  " + status,
                HpColor(pct));
        }

        /// <summary>Resolve a hit transform to a barricade or structure and read its HP/owner.</summary>
        private static bool Resolve(Transform t, out ushort cur, out ushort max,
                                    out ulong owner, out ulong group, out Vector3 point, out string name)
        {
            cur = max = 0; owner = group = 0; point = t.position; name = null;

            BarricadeDrop bd = BarricadeManager.FindBarricadeByRootTransform(t)
                               ?? BarricadeManager.FindBarricadeByRootTransform(t.root);
            if (bd != null && bd.asset != null)
            {
                BarricadeData d = bd.GetServersideData();
                if (d == null) return false;
                cur = d.barricade.health; max = bd.asset.health;
                owner = d.owner; group = d.group; point = d.point;
                name = bd.asset.itemName;
                return true;
            }

            StructureDrop sd = StructureManager.FindStructureByRootTransform(t)
                               ?? StructureManager.FindStructureByRootTransform(t.root);
            if (sd != null && sd.asset != null)
            {
                StructureData d = sd.GetServersideData();
                if (d == null) return false;
                cur = d.structure.health; max = sd.asset.health;
                owner = d.owner; group = d.group; point = d.point;
                name = sd.asset.itemName;
                return true;
            }

            return false;
        }

        private static Color HpColor(int pct)
        {
            if (pct >= 67) return Color.green;
            if (pct >= 34) return Color.yellow;
            return Color.red;
        }

        private static void Tell(Player player, string text, Color color)
        {
            ChatManager.serverSendMessage(
                text, color,
                fromPlayer: null,
                toPlayer: player.channel.owner,
                mode: EChatMode.SAY,
                iconURL: null,
                useRichTextFormatting: true);
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
