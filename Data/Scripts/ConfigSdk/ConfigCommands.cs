using System;
using Sandbox.ModAPI;

namespace ConfigSdk
{
    // Only /configreload remains; viewing and editing config is done in the Rich HUD menu.
    public class ConfigCommands
    {
        readonly ConfigSdkSession _s;

        public ConfigCommands(ConfigSdkSession session)
        {
            _s = session;
            MyAPIGateway.Utilities.MessageEntered += OnMessage;
        }

        public void Dispose()
        {
            MyAPIGateway.Utilities.MessageEntered -= OnMessage;
        }

        void OnMessage(string text, ref bool sendToOthers)
        {
            try
            {
                string t = text.Trim();
                if(t.Equals("/configreload", StringComparison.OrdinalIgnoreCase) || t.StartsWith("/configreload ", StringComparison.OrdinalIgnoreCase))
                {
                    sendToOthers = false;
                    ulong me = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
                    _s.ReloadAll(me);
                }
            }
            catch(Exception e) { Log.Error(e); }
        }
    }
}
