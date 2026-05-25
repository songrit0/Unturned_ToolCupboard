using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using UnityEngine;

namespace ToolCupboard
{
    /// <summary>
    /// <c>/toolcupboardreload</c> (alias <c>/tcreload</c>) - reloads the configuration from disk
    /// and rebuilds the decay engine with the new values. Permission node: <c>toolcupboard.reload</c>.
    /// </summary>
    public sealed class CommandToolCupboardReload : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Both;
        public string Name => "toolcupboardreload";
        public string Help => "Reloads the ToolCupboard plugin configuration.";
        public string Syntax => "";
        public List<string> Aliases => new List<string> { "tcreload" };
        public List<string> Permissions => new List<string> { "toolcupboard.reload" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            ToolCupboardPlugin plugin = ToolCupboardPlugin.Instance;
            if (plugin == null)
                return;

            plugin.ReloadConfiguration();
            UnturnedChat.Say(caller, "[ToolCupboard] Configuration reloaded.", Color.green);
        }
    }
}
