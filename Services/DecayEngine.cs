using System;
using System.Collections.Generic;
using Rocket.Core.Logging;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace ToolCupboard
{
    /// <summary>
    /// Drives the base-decay system. Two independent passes (decay + heal) each sweep every
    /// barricade and structure in the world, sliced across many FixedUpdate ticks so a large
    /// base count never stalls the server. Everything runs on the Unity main thread, so the
    /// SDG building APIs (which are not thread-safe) are always called safely.
    ///
    /// Per pass each buildable is touched exactly once, so the decay/heal rate is tied to the
    /// configured interval and not to how many buildings exist or the server frame rate.
    /// </summary>
    public sealed class DecayEngine
    {
        private readonly ToolCupboardConfiguration _cfg;
        private readonly ChatNotifier _notifier;

        // Snapshot of active protection bubbles, rebuilt at the start of each pass.
        private readonly List<ProtectionSource> _sources = new List<ProtectionSource>();

        private readonly SweepCursor _decay = new SweepCursor();
        private readonly SweepCursor _heal = new SweepCursor();
        private float _decayTimer;
        private float _healTimer;

        // Aggregated per-owner counts for the current decay pass (flushed when it finishes).
        private readonly Dictionary<ulong, int> _decayingCounts = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> _destroyedCounts = new Dictionary<ulong, int>();

        public DecayEngine(ToolCupboardConfiguration cfg, ChatNotifier notifier)
        {
            _cfg = cfg;
            _notifier = notifier;
        }

        /// <summary>Seconds remaining until the next decay pass begins (for /decay status).</summary>
        public float SecondsUntilDecay => Mathf.Max(0f, _cfg.Decay.DamageInterval - _decayTimer);

        // -----------------------------------------------------------------
        //  Main loop
        // -----------------------------------------------------------------

        public void Tick(float dt)
        {
            if (!Level.isLoaded)
                return;

            _decayTimer += dt;
            if (!_decay.Active && _decayTimer >= _cfg.Decay.DamageInterval)
            {
                RebuildSources();
                _decay.Start();
                _decayTimer = 0f;
            }
            if (_decay.Active)
                RunPass(_decay, isDecay: true);

            _healTimer += dt;
            if (!_heal.Active && _healTimer >= _cfg.Healing.HealingInterval)
            {
                RebuildSources();
                _heal.Start();
                _healTimer = 0f;
            }
            if (_heal.Active)
                RunPass(_heal, isDecay: false);
        }

        // -----------------------------------------------------------------
        //  Chunked sweep over barricade regions then structure regions
        // -----------------------------------------------------------------

        private void RunPass(SweepCursor c, bool isDecay)
        {
            int budget = _cfg.Decay.MaxBuildablesPerTick;
            if (budget <= 0)
                budget = 1;

            while (budget > 0 && c.Active)
            {
                if (c.Phase == 0)
                {
                    BarricadeRegion[,] regions = BarricadeManager.regions;
                    if (regions == null) { c.Phase = 1; c.X = 0; c.Y = 0; c.Idx = 0; continue; }
                    int xl = regions.GetLength(0), yl = regions.GetLength(1);
                    if (c.X >= xl) { c.Phase = 1; c.X = 0; c.Y = 0; c.Idx = 0; continue; }

                    List<BarricadeDrop> drops = regions[c.X, c.Y].drops;
                    if (drops == null || c.Idx >= drops.Count) { Advance(c, yl); continue; }

                    int before = drops.Count;
                    SafeProcessBarricade(drops[c.Idx], isDecay);
                    budget--;
                    // If the drop was destroyed it has been removed and everything shifted down,
                    // so keep the same index; otherwise step forward.
                    if (drops.Count >= before) c.Idx++;
                }
                else
                {
                    StructureRegion[,] regions = StructureManager.regions;
                    if (regions == null) { FinishPass(c, isDecay); break; }
                    int xl = regions.GetLength(0), yl = regions.GetLength(1);
                    if (c.X >= xl) { FinishPass(c, isDecay); break; }

                    List<StructureDrop> drops = regions[c.X, c.Y].drops;
                    if (drops == null || c.Idx >= drops.Count) { Advance(c, yl); continue; }

                    int before = drops.Count;
                    SafeProcessStructure(drops[c.Idx], isDecay);
                    budget--;
                    if (drops.Count >= before) c.Idx++;
                }
            }
        }

        private static void Advance(SweepCursor c, int yLen)
        {
            c.Idx = 0;
            c.Y++;
            if (c.Y >= yLen) { c.Y = 0; c.X++; }
        }

        private void FinishPass(SweepCursor c, bool isDecay)
        {
            c.Stop();
            if (isDecay)
                FlushNotifications();
        }

        // -----------------------------------------------------------------
        //  Per-buildable processing
        // -----------------------------------------------------------------

        private void SafeProcessBarricade(BarricadeDrop drop, bool isDecay)
        {
            try { ProcessBarricade(drop, isDecay); }
            catch (Exception ex) { Logger.LogWarning("ToolCupboard: barricade pass error: " + ex.Message); }
        }

        private void SafeProcessStructure(StructureDrop drop, bool isDecay)
        {
            try { ProcessStructure(drop, isDecay); }
            catch (Exception ex) { Logger.LogWarning("ToolCupboard: structure pass error: " + ex.Message); }
        }

        private void ProcessBarricade(BarricadeDrop drop, bool isDecay)
        {
            if (drop == null) return;
            BarricadeData data = drop.GetServersideData();
            ItemBarricadeAsset asset = drop.asset;
            if (data == null || asset == null) return;
            if (IsBypassed(asset.id)) return;

            ushort max = asset.health;
            ushort cur = data.barricade.health;
            if (max == 0) return;

            bool protect = IsProtected(data.point, data.owner, data.group);

            if (isDecay)
            {
                if (protect) return;
                float amt = DecayAmount(max);
                bool willDestroy = cur <= amt;
                BarricadeManager.damage(drop.model, amt, 1f, false, CSteamID.Nil, EDamageOrigin.Unknown);
                Account(data.owner, max, cur, amt, willDestroy);
            }
            else
            {
                if (!protect || cur >= max) return;
                BarricadeManager.repair(drop.model, HealAmount(max), 1f, CSteamID.Nil);
            }
        }

        private void ProcessStructure(StructureDrop drop, bool isDecay)
        {
            if (drop == null) return;
            StructureData data = drop.GetServersideData();
            ItemStructureAsset asset = drop.asset;
            if (data == null || asset == null) return;
            if (IsBypassed(asset.id)) return;

            ushort max = asset.health;
            ushort cur = data.structure.health;
            if (max == 0) return;

            bool protect = IsProtected(data.point, data.owner, data.group);

            if (isDecay)
            {
                if (protect) return;
                float amt = DecayAmount(max);
                bool willDestroy = cur <= amt;
                StructureManager.damage(drop.model, Vector3.up, amt, 1f, false, CSteamID.Nil, EDamageOrigin.Unknown);
                Account(data.owner, max, cur, amt, willDestroy);
            }
            else
            {
                if (!protect || cur >= max) return;
                StructureManager.repair(drop.model, HealAmount(max), 1f, CSteamID.Nil);
            }
        }

        /// <summary>Record a decayed buildable for the end-of-pass owner notifications.</summary>
        private void Account(ulong owner, ushort max, ushort cur, float amt, bool willDestroy)
        {
            if (owner == 0) return;
            if (willDestroy)
            {
                Bump(_destroyedCounts, owner);
                return;
            }
            float after = cur - amt;
            if (after <= max * (_cfg.WarnHealthThreshold / 100f))
                Bump(_decayingCounts, owner);
        }

        private float DecayAmount(ushort max)
        {
            float amt = _cfg.Decay.UsePercentage ? max * (_cfg.Decay.DamagePerInterval / 100f) : _cfg.Decay.DamagePerInterval;
            return amt < 1f ? 1f : amt; // guarantee progress even with tiny percentages
        }

        private float HealAmount(ushort max)
        {
            float amt = _cfg.Healing.UsePercentage ? max * (_cfg.Healing.HealingPerInterval / 100f) : _cfg.Healing.HealingPerInterval;
            return amt < 1f ? 1f : amt;
        }

        // -----------------------------------------------------------------
        //  Protection lookup
        // -----------------------------------------------------------------

        /// <summary>True if <paramref name="point"/> is inside a protection bubble that applies to this owner/group.</summary>
        public bool IsProtected(Vector3 point, ulong owner, ulong group)
        {
            EProtectionType _;
            return TryGetProtection(point, owner, group, out _);
        }

        /// <summary>Protection lookup that also reports which device type covers the point. Rebuilds the source list.</summary>
        public bool QueryProtection(Vector3 point, ulong owner, ulong group, out EProtectionType type)
        {
            RebuildSources();
            return TryGetProtection(point, owner, group, out type);
        }

        /// <summary>Refresh the cached protection bubbles (call once before a batch of IsProtected checks).</summary>
        public void RefreshSources() => RebuildSources();

        /// <summary>
        /// Returns the protection bubbles owned by this player (or their group) whose centre is within
        /// <paramref name="withinRange"/> of <paramref name="near"/>. Used by /decay to draw radius rings
        /// for only the caller's own devices. Rebuilds the source snapshot first.
        /// </summary>
        public List<ProtectionSource> GetOwnedSourcesNear(Vector3 near, ulong owner, ulong group, float withinRange)
        {
            RebuildSources();

            List<ProtectionSource> result = new List<ProtectionSource>();
            if (owner == 0 && group == 0)
                return result;

            float r2 = withinRange * withinRange;
            for (int i = 0; i < _sources.Count; i++)
            {
                ProtectionSource s = _sources[i];
                bool mine = (owner != 0 && s.Owner == owner) || (group != 0 && s.Group == group);
                if (!mine)
                    continue;
                if ((s.Center - near).sqrMagnitude > r2)
                    continue;
                result.Add(s);
            }
            return result;
        }

        private static bool OwnedBy(ulong dataOwner, ulong dataGroup, ulong owner, ulong group)
        {
            return (owner != 0 && dataOwner == owner) || (group != 0 && dataGroup == group);
        }

        /// <summary>True if a barricade/structure owned by this player (or their group) sits within <paramref name="radius"/> of <paramref name="pos"/>.</summary>
        public bool HasOwnedBuildableNear(Vector3 pos, ulong owner, ulong group, float radius)
        {
            if (!Level.isLoaded || (owner == 0 && group == 0))
                return false;
            if (!Regions.tryGetCoordinate(pos, out byte bx, out byte by))
                return false;

            float r2 = radius * radius;

            BarricadeRegion[,] br = BarricadeManager.regions;
            if (br != null)
            {
                int xl = br.GetLength(0), yl = br.GetLength(1);
                for (int x = Mathf.Max(0, bx - 1); x <= Mathf.Min(xl - 1, bx + 1); x++)
                for (int y = Mathf.Max(0, by - 1); y <= Mathf.Min(yl - 1, by + 1); y++)
                {
                    List<BarricadeDrop> drops = br[x, y].drops;
                    if (drops == null) continue;
                    for (int i = 0; i < drops.Count; i++)
                    {
                        BarricadeData d = drops[i]?.GetServersideData();
                        if (d == null || !OwnedBy(d.owner, d.group, owner, group)) continue;
                        if ((d.point - pos).sqrMagnitude <= r2) return true;
                    }
                }
            }

            StructureRegion[,] sr = StructureManager.regions;
            if (sr != null)
            {
                int xl = sr.GetLength(0), yl = sr.GetLength(1);
                for (int x = Mathf.Max(0, bx - 1); x <= Mathf.Min(xl - 1, bx + 1); x++)
                for (int y = Mathf.Max(0, by - 1); y <= Mathf.Min(yl - 1, by + 1); y++)
                {
                    List<StructureDrop> drops = sr[x, y].drops;
                    if (drops == null) continue;
                    for (int i = 0; i < drops.Count; i++)
                    {
                        StructureData d = drops[i]?.GetServersideData();
                        if (d == null || !OwnedBy(d.owner, d.group, owner, group)) continue;
                        if ((d.point - pos).sqrMagnitude <= r2) return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetProtection(Vector3 point, ulong owner, ulong group, out EProtectionType type)
        {
            // Linear scan: protection devices are few relative to total buildables, and the
            // squared-distance test is cheap. (Bucket by region later if device counts grow huge.)
            for (int i = 0; i < _sources.Count; i++)
            {
                ProtectionSource s = _sources[i];
                if ((point - s.Center).sqrMagnitude > s.RadiusSqr)
                    continue;
                if (!_cfg.Protection.RequireSameOwner
                    || (owner != 0 && owner == s.Owner)
                    || (group != 0 && group == s.Group))
                {
                    type = s.Type;
                    return true;
                }
            }
            type = EProtectionType.ClaimFlag;
            return false;
        }

        // -----------------------------------------------------------------
        //  Build the list of active protection bubbles from barricade regions
        // -----------------------------------------------------------------

        public void RebuildSources()
        {
            _sources.Clear();
            if (!Level.isLoaded)
                return;

            BarricadeRegion[,] regions = BarricadeManager.regions;
            if (regions == null)
                return;

            int xl = regions.GetLength(0), yl = regions.GetLength(1);
            ProtectionSettings p = _cfg.Protection;

            for (int x = 0; x < xl; x++)
            for (int y = 0; y < yl; y++)
            {
                List<BarricadeDrop> drops = regions[x, y].drops;
                if (drops == null) continue;

                for (int i = 0; i < drops.Count; i++)
                {
                    BarricadeDrop drop = drops[i];
                    BarricadeData data = drop?.GetServersideData();
                    if (data == null) continue;

                    Interactable inter = drop.interactable;
                    ushort id = drop.asset != null ? drop.asset.id : (ushort)0;

                    // Custom items take priority: a configured id ALWAYS protects when placed,
                    // regardless of interactable type or power state. Use this to register modded
                    // generators (or anything else) that the type checks below would miss.
                    float cr = CustomRadius(id);
                    if (cr > 0f)
                    {
                        AddSource(data, cr, EProtectionType.CustomItem);
                        continue;
                    }

                    if (p.UseClaimFlags && inter is InteractableClaim)
                    {
                        AddSource(data, p.ClaimFlagRadius, EProtectionType.ClaimFlag);
                        continue;
                    }
                    if (p.UseGenerators && inter is InteractableGenerator gen)
                    {
                        if (gen.isPowered && (!p.RequireFuel || gen.fuel > 0))
                            AddSource(data, p.GeneratorRadius, EProtectionType.Generator);
                        continue;
                    }
                    if (p.UseBeds && inter is InteractableBed bed)
                    {
                        if (!p.RequireClaimed || bed.owner.m_SteamID != 0)
                            AddSource(data, p.BedRadius, EProtectionType.Bed);
                        continue;
                    }
                }
            }
        }

        private void AddSource(BarricadeData data, float radius, EProtectionType type)
        {
            if (radius <= 0f) return;
            _sources.Add(new ProtectionSource
            {
                Center = data.point,
                RadiusSqr = radius * radius,
                Owner = data.owner,
                Group = data.group,
                Type = type
            });
        }

        private float CustomRadius(ushort id)
        {
            CustomItem[] items = _cfg.Protection.CustomItems;
            if (items == null) return 0f;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].Id == id)
                    return items[i].Radius;
            return 0f;
        }

        private bool IsBypassed(ushort id)
        {
            ushort[] ids = _cfg.BypassItemIds;
            if (ids == null) return false;
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == id)
                    return true;
            return false;
        }

        // -----------------------------------------------------------------
        //  Notifications
        // -----------------------------------------------------------------

        private static void Bump(Dictionary<ulong, int> map, ulong owner)
        {
            map.TryGetValue(owner, out int n);
            map[owner] = n + 1;
        }

        private void FlushNotifications()
        {
            foreach (KeyValuePair<ulong, int> kv in _destroyedCounts)
                _notifier.NotifyDestroyed(kv.Key, kv.Value);
            foreach (KeyValuePair<ulong, int> kv in _decayingCounts)
                _notifier.WarnDecaying(kv.Key, kv.Value);
            _decayingCounts.Clear();
            _destroyedCounts.Clear();
        }

        /// <summary>Mutable cursor for one in-progress sweep. Phase 0 = barricades, 1 = structures.</summary>
        private sealed class SweepCursor
        {
            public bool Active;
            public int Phase;
            public int X, Y, Idx;

            public void Start() { Active = true; Phase = 0; X = 0; Y = 0; Idx = 0; }
            public void Stop() { Active = false; }
        }
    }
}
