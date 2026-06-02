using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace ToolCupboard
{
    /// <summary>
    /// Rust-style base decay for Unturned + RocketMod, with no UI - all feedback is sent
    /// through chat. Unprotected barricades/structures lose HP over time; buildables inside
    /// the radius of a protection device (Claim Flag, powered Generator, claimed Bed, or a
    /// configured Custom Item) are healed instead.
    ///
    /// RocketMod plugins are MonoBehaviours, so FixedUpdate below runs on the Unity main
    /// thread alongside all building APIs - no locking is needed.
    /// </summary>
    public sealed class ToolCupboardPlugin : RocketPlugin<ToolCupboardConfiguration>
    {
        public static ToolCupboardPlugin Instance { get; private set; }

        /// <summary>The decay/heal engine. Null while the plugin is unloaded.</summary>
        public DecayEngine Engine { get; private set; }

        /// <summary>Draws protection-radius rings for /decay. Null while the plugin is unloaded.</summary>
        public RingDisplayService Rings { get; private set; }

        private ChatNotifier _notifier;
        private PresenceNotifier _presence;
        private float _presenceTimer;

        protected override void Load()
        {
            Instance = this;
            BuildEngine();
            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;

            ToolCupboardConfiguration cfg = Configuration.Instance;
            Logger.Log("ToolCupboard loaded. DecayInterval=" + cfg.Decay.DamageInterval +
                       "s (" + cfg.Decay.DamagePerInterval + (cfg.Decay.UsePercentage ? "%" : "hp") +
                       "), HealInterval=" + cfg.Healing.HealingInterval +
                       "s, WarnOnBaseEnter=" + cfg.WarnOnBaseEnter + ".");
        }

        protected override void Unload()
        {
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            _notifier?.Clear();
            _presence?.Clear();
            Rings?.Clear();
            Engine = null;
            Rings = null;
            _notifier = null;
            _presence = null;
            Instance = null;
            Logger.Log("ToolCupboard unloaded.");
        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            _presence?.Remove(player.CSteamID.m_SteamID);
        }

        /// <summary>Reload config from disk and rebuild the engine so new values take effect.</summary>
        public void ReloadConfiguration()
        {
            Configuration.Load();
            BuildEngine();
        }

        private void BuildEngine()
        {
            // Backfill the Visual section for configs written before it existed, so /decay can't NRE.
            if (Configuration.Instance.Visual == null)
                Configuration.Instance.Visual = VisualSettings.Default();

            _notifier = new ChatNotifier(Configuration.Instance);
            Engine = new DecayEngine(Configuration.Instance, _notifier);
            _presence = new PresenceNotifier(Configuration.Instance, Engine);
            Rings = new RingDisplayService(Configuration.Instance);
            _presenceTimer = 0f;
        }

        private void FixedUpdate()
        {
            if (Engine == null || !Level.isLoaded)
                return;

            float dt = Time.fixedDeltaTime;
            Engine.Tick(dt);
            Rings.Tick(dt);

            ToolCupboardConfiguration cfg = Configuration.Instance;
            if (cfg.WarnOnBaseEnter)
            {
                _presenceTimer += dt;
                if (_presenceTimer >= cfg.PresenceCheckInterval)
                {
                    _presenceTimer = 0f;
                    _presence.Check();
                }
            }
        }
    }
}
