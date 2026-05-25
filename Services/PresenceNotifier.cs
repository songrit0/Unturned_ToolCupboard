using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace ToolCupboard
{
    /// <summary>
    /// Real-time, presence-based warning: the moment a player steps onto (or near) their OWN
    /// base while that spot is NOT protected, they get a single chat warning. It is edge-triggered
    /// on the status change, so standing still does not spam, and being protected stays silent.
    /// </summary>
    public sealed class PresenceNotifier
    {
        private const int StateAway = 0;        // not near any of their own buildings (default)
        private const int StateProtected = 1;   // on base, inside a protection bubble
        private const int StateUnprotected = 2; // on base, will decay

        private readonly ToolCupboardConfiguration _cfg;
        private readonly DecayEngine _engine;
        private readonly Dictionary<ulong, int> _lastState = new Dictionary<ulong, int>();

        public PresenceNotifier(ToolCupboardConfiguration cfg, DecayEngine engine)
        {
            _cfg = cfg;
            _engine = engine;
        }

        public void Check()
        {
            if (!_cfg.WarnOnBaseEnter)
                return;

            // Refresh the protection bubbles once, then reuse them for every online player.
            _engine.RefreshSources();

            List<SteamPlayer> clients = Provider.clients;
            for (int i = 0; i < clients.Count; i++)
            {
                SteamPlayer sp = clients[i];
                Player p = sp?.player;
                if (p == null)
                    continue;

                ulong owner = sp.playerID.steamID.m_SteamID;
                ulong group = p.quests.groupID.m_SteamID;
                Vector3 pos = p.transform.position;

                int state;
                if (!_engine.HasOwnedBuildableNear(pos, owner, group, _cfg.BaseNearRadius))
                    state = StateAway;
                else if (_engine.IsProtected(pos, owner, group))
                    state = StateProtected;
                else
                    state = StateUnprotected;

                _lastState.TryGetValue(owner, out int prev);
                if (state == StateUnprotected && prev != StateUnprotected)
                    ChatNotifier.Send(p, _cfg.MsgStatusUnprotected, how: ChatNotifier.BuildHowToProtect(_cfg));

                _lastState[owner] = state;
            }
        }

        public void Remove(ulong steamId) => _lastState.Remove(steamId);

        public void Clear() => _lastState.Clear();
    }
}
