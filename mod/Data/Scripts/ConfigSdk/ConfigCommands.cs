using System;
using Sandbox.ModAPI;

namespace ConfigSdk
{
    // /configreload and /configreset; viewing and editing config is done in the Rich HUD menu.
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
                else if(t.Equals("/configreset", StringComparison.OrdinalIgnoreCase) || t.StartsWith("/configreset ", StringComparison.OrdinalIgnoreCase))
                {
                    sendToOthers = false;
                    ulong me = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
                    _s.ResetAll(me);
                }
            }
            catch(Exception e) { Log.Error(e); }
        }
    }
}
