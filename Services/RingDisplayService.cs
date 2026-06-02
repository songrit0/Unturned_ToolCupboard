using System.Collections.Generic;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace ToolCupboard
{
    /// <summary>
    /// Draws temporary horizontal rings of effect points to show protection radii. Each ring burst
    /// belongs to one player and is re-emitted every <c>RingInterval</c> for <c>RingDurationSeconds</c>
    /// so the ring lingers instead of flashing for a single frame. Points are sent ONLY to that player
    /// (via sendEffectReliable), so other players never see them and network cost stays bounded.
    ///
    /// Ticked from the plugin's FixedUpdate, so all calls happen on the Unity main thread.
    /// </summary>
    public sealed class RingDisplayService
    {
        private sealed class Ring
        {
            public Vector3 Center;
            public float Radius;
        }

        private sealed class Burst
        {
            public ulong SteamId;
            public List<Ring> Rings;
            public float Remaining;
            public float Accumulator;
        }

        private readonly ToolCupboardConfiguration _cfg;
        private readonly List<Burst> _bursts = new List<Burst>();

        private bool _missingEffectWarned;

        public RingDisplayService(ToolCupboardConfiguration cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// Starts (or restarts) the ring display for one player, drawing a ring at each of the given
        /// centre/radius pairs. Running /decay again simply replaces that player's burst.
        /// </summary>
        public void Show(ulong steamId, IList<KeyValuePair<Vector3, float>> rings)
        {
            VisualSettings v = _cfg.Visual;
            if (!v.ShowProtectionRings || v.RingEffectId == 0 || rings == null || rings.Count == 0)
                return;

            List<Ring> list = new List<Ring>(rings.Count);
            foreach (KeyValuePair<Vector3, float> r in rings)
            {
                if (r.Value > 0f)
                    list.Add(new Ring { Center = r.Key, Radius = r.Value });
            }
            if (list.Count == 0)
                return;

            _bursts.RemoveAll(b => b.SteamId == steamId);
            _bursts.Add(new Burst
            {
                SteamId = steamId,
                Rings = list,
                Remaining = v.RingDurationSeconds,
                // Pre-fill the accumulator so the first ring draws on the very next tick.
                Accumulator = v.RingInterval
            });
        }

        public void Tick(float dt)
        {
            if (_bursts.Count == 0)
                return;

            VisualSettings v = _cfg.Visual;
            float interval = v.RingInterval > 0f ? v.RingInterval : 0.5f;

            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                Burst b = _bursts[i];
                b.Remaining -= dt;

                SteamPlayer sp = PlayerTool.getSteamPlayer(new CSteamID(b.SteamId));
                if (sp == null || b.Remaining <= 0f)
                {
                    _bursts.RemoveAt(i);
                    continue;
                }

                b.Accumulator += dt;
                if (b.Accumulator < interval)
                    continue;
                b.Accumulator = 0f;

                ITransportConnection conn = sp.transportConnection;
                for (int j = 0; j < b.Rings.Count; j++)
                    EmitRing(conn, b.Rings[j]);
            }
        }

        private void EmitRing(ITransportConnection conn, Ring ring)
        {
            VisualSettings v = _cfg.Visual;

            // Resolve the asset and use the asset-based TriggerEffectParameters (the id-based
            // sendEffect API is obsolete in this Unturned build).
            EffectAsset asset = Assets.find(EAssetType.EFFECT, v.RingEffectId) as EffectAsset;
            if (asset == null)
            {
                if (!_missingEffectWarned)
                {
                    _missingEffectWarned = true;
                    Logger.LogWarning($"ToolCupboard: ring effect id {v.RingEffectId} not found; protection rings disabled until fixed.");
                }
                return;
            }

            // Scale point count to the circumference so small and large radii both look even,
            // capped so a big radius can't flood the network.
            float spacing = v.RingPointSpacing > 0f ? v.RingPointSpacing : 2.5f;
            int points = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * ring.Radius / spacing), 8, Mathf.Max(8, v.RingMaxPoints));

            Vector3 center = ring.Center + new Vector3(0f, v.RingYOffset, 0f);
            float twoPi = Mathf.PI * 2f;
            for (int i = 0; i < points; i++)
            {
                float a = twoPi * i / points;
                Vector3 pos = center + new Vector3(ring.Radius * Mathf.Cos(a), 0f, ring.Radius * Mathf.Sin(a));

                TriggerEffectParameters parameters = new TriggerEffectParameters(asset)
                {
                    position = pos,
                    reliable = true
                };
                parameters.SetRelevantPlayer(conn); // send ONLY to the caller
                EffectManager.triggerEffect(parameters);
            }
        }

        public void Clear()
        {
            _bursts.Clear();
        }
    }
}
