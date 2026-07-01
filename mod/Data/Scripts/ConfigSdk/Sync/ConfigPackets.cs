using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;

namespace ConfigSdk.Sync
{
    [ProtoContract]
    [ProtoInclude(10, typeof(RequestServerConfigPacket))]
    [ProtoInclude(11, typeof(ServerConfigPacket))]
    [ProtoInclude(12, typeof(SetServerValuePacket))]
    [ProtoInclude(13, typeof(ReloadServerPacket))]
    [ProtoInclude(14, typeof(ResetServerPacket))]
    public abstract partial class PacketBase
    {
        [ProtoMember(1)] public ulong OriginalSenderSteamId;

        protected PacketBase() { OriginalSenderSteamId = MyAPIGateway.Multiplayer.MyId; }

        public abstract void Received(ref RelayMode relay, ulong senderSteamId);
    }

    // client -> server: "send me this mod's server-scope values"
    [ProtoContract]
    public class RequestServerConfigPacket : PacketBase
    {
        [ProtoMember(2)] public string ModId;

        public RequestServerConfigPacket() { }
        public RequestServerConfigPacket(string modId) { ModId = modId; }

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            ConfigSdkSession s = ConfigSdkSession.Instance;
            if(s == null || !s.IsServer) return;
            RegisteredMod reg;
            if(s.Mods.TryGetValue(ModId, out reg))
                s.Network.SendServerConfigTo(reg, OriginalSenderSteamId);
        }
    }

    // server -> client(s): current server-scope values for a mod
    [ProtoContract]
    public class ServerConfigPacket : PacketBase
    {
        [ProtoMember(2)] public string ModId;
        [ProtoMember(3)] public List<string> Keys = new List<string>();
        [ProtoMember(4)] public List<string> Values = new List<string>();

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            ConfigSdkSession s = ConfigSdkSession.Instance;
            if(s == null) return;
            RegisteredMod reg;
            if(!s.Mods.TryGetValue(ModId, out reg)) return;

            for(int i = 0; i < Keys.Count && i < Values.Count; i++)
            {
                ConfigItem item;
                if(reg.Items.TryGetValue(Keys[i], out item) && item.Scope == ConfigScope.Server)
                {
                    bool ok;
                    item.Current = item.Parse(Values[i], out ok);
                    reg.Push(item);
                }
            }
        }
    }

    // client(admin) -> server: change a server-scope value authoritatively
    [ProtoContract]
    public class SetServerValuePacket : PacketBase
    {
        [ProtoMember(2)] public string ModId;
        [ProtoMember(3)] public string Key;
        [ProtoMember(4)] public string Value;

        public SetServerValuePacket() { }
        public SetServerValuePacket(string modId, string key, string value) { ModId = modId; Key = key; Value = value; }

        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            ConfigSdkSession s = ConfigSdkSession.Instance;
            if(s == null || !s.IsServer) return;
            if(!s.IsAdmin(OriginalSenderSteamId)) return; // only admins change server settings
            string error;
            s.RequestSetValue(ModId, Key, Value, OriginalSenderSteamId, out error); // applies + broadcasts
        }
    }

    // client(admin) -> server: reload server config files from disk and rebroadcast
    [ProtoContract]
    public class ReloadServerPacket : PacketBase
    {
        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            ConfigSdkSession s = ConfigSdkSession.Instance;
            if(s == null || !s.IsServer) return;
            if(!s.IsAdmin(OriginalSenderSteamId)) return;
            s.ServerReloadAndBroadcast(OriginalSenderSteamId);
        }
    }

    // client(admin) -> server: reset server-scope values to defaults and rebroadcast
    [ProtoContract]
    public class ResetServerPacket : PacketBase
    {
        public override void Received(ref RelayMode relay, ulong senderSteamId)
        {
            ConfigSdkSession s = ConfigSdkSession.Instance;
            if(s == null || !s.IsServer) return;
            if(!s.IsAdmin(OriginalSenderSteamId)) return;
            s.ServerResetAndBroadcast(OriginalSenderSteamId);
        }
    }

    // Thin wrapper around Networking with config-specific helpers.
    public class ConfigNetwork
    {
        const ushort NetChannel = 27306;

        readonly ConfigSdkSession _session;
        readonly Networking _net = new Networking(NetChannel);

        public ConfigNetwork(ConfigSdkSession session) { _session = session; }

        public void Register() { _net.Register(); }
        public void Unregister() { _net.Unregister(); }

        // client -> server
        public void RequestServerConfig(string modId) { _net.SendToServer(new RequestServerConfigPacket(modId)); }
        public void SendSetServerValue(string modId, string key, string value) { _net.SendToServer(new SetServerValuePacket(modId, key, value)); }
        public void SendReloadRequest() { _net.SendToServer(new ReloadServerPacket()); }
        public void SendResetRequest() { _net.SendToServer(new ResetServerPacket()); }

        // server -> client(s)
        public void SendServerConfigTo(RegisteredMod reg, ulong steamId) { _net.SendToPlayer(Build(reg), steamId); }
        public void BroadcastServerConfig(RegisteredMod reg) { _net.SendToOthers(Build(reg)); }

        static ServerConfigPacket Build(RegisteredMod reg)
        {
            ServerConfigPacket p = new ServerConfigPacket { ModId = reg.ModId };
            foreach(ConfigItem item in reg.Items.Values)
            {
                if(item.Scope != ConfigScope.Server) continue;
                p.Keys.Add(item.Key);
                p.Values.Add(item.CurrentString);
            }
            return p;
        }
    }
}
