using System.Xml.Serialization;
using Rocket.API;

namespace ToolCupboard
{
    /// <summary>One entry in the Custom Item protection list (asset id + radius).</summary>
    public sealed class CustomItem
    {
        [XmlAttribute] public ushort Id;
        [XmlAttribute] public float Radius;

        public CustomItem() { }
        public CustomItem(ushort id, float radius) { Id = id; Radius = radius; }
    }

    /// <summary>Damage applied to UNPROTECTED buildables once per <see cref="DamageInterval"/>.</summary>
    public sealed class DecaySettings
    {
        /// <summary>Seconds between decay passes. Each unprotected buildable loses HP once per pass.</summary>
        public float DamageInterval;

        /// <summary>true = DamagePerInterval is a percentage of max health; false = flat HP.</summary>
        public bool UsePercentage;

        /// <summary>Percent (0-100) or flat HP removed per pass. Always at least 1 HP effective.</summary>
        public float DamagePerInterval;

        /// <summary>
        /// How many buildables to process per FixedUpdate tick. The whole world is swept in
        /// slices so a big base count never causes a frame spike. Higher = faster sweep, more
        /// work per tick. 200 is a safe default.
        /// </summary>
        public ushort MaxBuildablesPerTick;
    }

    /// <summary>Healing applied to PROTECTED buildables once per <see cref="HealingInterval"/>.</summary>
    public sealed class HealingSettings
    {
        public float HealingInterval;
        public bool UsePercentage;
        public float HealingPerInterval;
    }

    /// <summary>Which devices create protection bubbles, and how big.</summary>
    public sealed class ProtectionSettings
    {
        /// <summary>
        /// true  = a device only protects buildables owned by the same player OR group
        ///         (prevents a stranger's flag from shielding/feeding your base, and stops
        ///          your flag from protecting an enemy base placed inside its radius).
        /// false = a device protects everything inside its radius regardless of owner.
        /// </summary>
        public bool RequireSameOwner;

        public bool UseClaimFlags;
        /// <summary>Protection radius (metres) of a Claim Flag. Vanilla claim radius is 32.</summary>
        public float ClaimFlagRadius;

        public bool UseGenerators;
        /// <summary>Protection radius of a POWERED generator (metres).</summary>
        public float GeneratorRadius;
        /// <summary>If true a generator must also have fuel &gt; 0 to protect.</summary>
        public bool RequireFuel;

        public bool UseBeds;
        public float BedRadius;
        /// <summary>If true a bed only protects once it has been claimed as a spawn point.</summary>
        public bool RequireClaimed;

        /// <summary>Arbitrary barricades/structures that act as protection devices.</summary>
        [XmlArray("CustomItems")]
        [XmlArrayItem("CustomItem")]
        public CustomItem[] CustomItems;
    }

    /// <summary>
    /// Effect ring shown by <c>/decay</c> to visualise the protection radius of the caller's own
    /// (or their group's) protection devices. The ring is sent only to the caller, so it never
    /// spams other players or reveals enemy bubbles.
    /// </summary>
    public sealed class VisualSettings
    {
        /// <summary>Master toggle: draw the protection radius ring on /decay.</summary>
        public bool ShowProtectionRings;

        /// <summary>EffectAsset id used for each ring point. Verify it exists on your server.</summary>
        public ushort RingEffectId;

        /// <summary>Only the caller's devices within this distance (metres) get a ring drawn.</summary>
        public float RingDisplayRange;

        /// <summary>How long (seconds) the ring keeps re-drawing after /decay is used.</summary>
        public float RingDurationSeconds;

        /// <summary>Seconds between ring re-draws while it is showing (smaller = smoother, more packets).</summary>
        public float RingInterval;

        /// <summary>Vertical offset of the ring relative to the device; negative sinks it toward the ground.</summary>
        public float RingYOffset;

        /// <summary>Target spacing (metres) between points on the ring; point count scales with radius.</summary>
        public float RingPointSpacing;

        /// <summary>Hard cap on points per ring so a large radius can't flood the network.</summary>
        public int RingMaxPoints;

        /// <summary>Default visual settings, used by LoadDefaults and to backfill older configs.</summary>
        public static VisualSettings Default()
        {
            return new VisualSettings
            {
                ShowProtectionRings = true,
                RingEffectId = 130,        // small marker effect; change to taste / verify on your server
                RingDisplayRange = 48f,    // draw rings for own devices within 48m of the caller
                RingDurationSeconds = 5f,  // ring lingers ~5s after /decay
                RingInterval = 0.5f,
                RingYOffset = 0.5f,
                RingPointSpacing = 2.5f,   // a point roughly every 2.5m around the circle
                RingMaxPoints = 64
            };
        }
    }

    /// <summary>
    /// Configuration for the ToolCupboard plugin. RocketMod serializes the public
    /// fields below to/from <c>Plugins/ToolCupboard/ToolCupboard.configuration.xml</c>.
    /// </summary>
    public sealed class ToolCupboardConfiguration : IRocketPluginConfiguration
    {
        public DecaySettings Decay;
        public HealingSettings Healing;
        public ProtectionSettings Protection;
        public VisualSettings Visual;

        /// <summary>Also decay/heal barricades placed on vehicles. Off by default (edge cases).</summary>
        public bool IncludeVehicleBarricades;

        /// <summary>Asset ids that never decay (e.g. the protection devices themselves, traps...).</summary>
        [XmlArray("BypassItemIds")]
        [XmlArrayItem("Id")]
        public ushort[] BypassItemIds;

        /// <summary>Warn the owner once their buildable drops below this percent of max HP.</summary>
        public float WarnHealthThreshold;

        /// <summary>Minimum seconds between chat warnings to the same owner (anti-spam).</summary>
        public float WarnCooldown;

        /// <summary>Warn a player the moment they step onto/near their OWN unprotected base.</summary>
        public bool WarnOnBaseEnter;

        /// <summary>Seconds between presence checks for the "entered unprotected base" warning.</summary>
        public float PresenceCheckInterval;

        /// <summary>How close (metres) a player must be to one of their own buildings to count as "on base".</summary>
        public float BaseNearRadius;

        // --- Player-facing messages (Text + Color attributes) ---

        /// <summary>Sent to an online owner when their base is decaying. Placeholder: {count}.</summary>
        public Message MsgDecaying;

        /// <summary>Sent to an online owner when decay destroyed parts of their base. Placeholder: {count}.</summary>
        public Message MsgDestroyed;

        /// <summary>/decay reply when standing inside a protection bubble. Placeholder: {type}.</summary>
        public Message MsgStatusProtected;

        /// <summary>/decay reply when standing outside any protection bubble.</summary>
        public Message MsgStatusUnprotected;

        /// <summary>/tcreload confirmation.</summary>
        public Message MsgReloaded;

        public void LoadDefaults()
        {
            Decay = new DecaySettings
            {
                DamageInterval = 3600f,   // 1 hour
                UsePercentage = true,
                DamagePerInterval = 5f,   // 5% of max HP per hour
                MaxBuildablesPerTick = 200
            };

            Healing = new HealingSettings
            {
                HealingInterval = 1800f,  // 30 minutes
                UsePercentage = true,
                HealingPerInterval = 10f  // 10% of max HP per pass
            };

            Protection = new ProtectionSettings
            {
                RequireSameOwner = true,
                UseClaimFlags = false,   // only Generator protects by default
                ClaimFlagRadius = 32f,
                UseGenerators = true,
                GeneratorRadius = 16f,
                RequireFuel = false,
                UseBeds = false,
                BedRadius = 16f,
                RequireClaimed = true,
                CustomItems = new CustomItem[0]
            };

            Visual = VisualSettings.Default();

            IncludeVehicleBarricades = false;
            BypassItemIds = new ushort[0];
            WarnHealthThreshold = 50f;
            WarnCooldown = 300f;
            WarnOnBaseEnter = true;
            PresenceCheckInterval = 3f;
            BaseNearRadius = 8f;

            MsgDecaying = new Message(
                "<color=#ff6666>⚠ Your base is decaying ({count} parts)! | ฐานของคุณกำลังผุ {count} ชิ้น!  How to protect: {how}</color>", "red");
            MsgDestroyed = new Message(
                "Parts of your base decayed away ({count}) | บางส่วนของฐานพังจากการผุ ({count} ชิ้น)  How to protect: {how}", "red");
            MsgStatusProtected = new Message(
                "✅ This spot is PROTECTED by: {type} | จุดนี้ป้องกันอยู่ โดย: {type}", "green");
            MsgStatusUnprotected = new Message(
                "⚠ NOT protected - buildings here will decay! | จุดนี้ไม่มีการป้องกัน สิ่งก่อสร้างจะผุ!  How to protect: {how}", "yellow");
            MsgReloaded = new Message(
                "[ToolCupboard] Configuration reloaded.", "green");
        }
    }
}
