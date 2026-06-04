using UnityEngine;

namespace ToolCupboard
{
    /// <summary>What kind of device produced a protection bubble (for status messages).</summary>
    public enum EProtectionType
    {
        ClaimFlag,
        Generator,
        Bed,
        CustomItem
    }

    /// <summary>
    /// A spherical protection zone produced by one device. Pure value type so the
    /// decay/heal passes can scan a flat list with cheap squared-distance checks.
    /// </summary>
    public struct ProtectionSource
    {
        public Vector3 Center;
        public float RadiusSqr;
        public ulong Owner;
        public ulong Group;
        public EProtectionType Type;

        /// <summary>Custom-item only: buildables inside this bubble are immune to ALL damage (raid + decay).</summary>
        public bool Invincible;

        /// <summary>Custom-item only: heal this percent of max HP per pass (0 = use the global Healing setting).</summary>
        public float HealPercent;
    }
}
