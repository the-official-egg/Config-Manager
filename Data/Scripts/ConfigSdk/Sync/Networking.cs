using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace ConfigSdk.Sync
{
    // Reusable ProtoBuf packet transport (server-authoritative with optional relay). Standard Digi pattern.
    public class Networking
    {
        public static bool IsPlayer => !MyAPIGateway.Utilities.IsDedicated;

        public readonly ushort ChannelId;
        List<IMyPlayer> TempPlayers;

        public Networking(ushort channelId) { ChannelId = channelId; }

        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            if(MyAPIGateway.Multiplayer.IsServer)
            {
                HandlePacket(packet, MyAPIGateway.Multiplayer.MyId);
                return;
            }
            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, serialized);
        }

        public void SendToPlayer(PacketBase packet, ulong steamId, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Clients can't send packets to other clients directly!");
            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, steamId);
        }

        public void SendToOthers(PacketBase packet)
        {
            RelayToOthers(packet, null);
        }

        void RelayToOthers(PacketBase packet, byte[] serialized, ulong senderSteamId = 0)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Clients can't broadcast directly!");

            if(TempPlayers == null)
                TempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            else
                TempPlayers.Clear();

            MyAPIGateway.Players.GetPlayers(TempPlayers);

            foreach(IMyPlayer p in TempPlayers)
            {
                if(p.SteamUserId == MyAPIGateway.Multiplayer.ServerId || p.SteamUserId == senderSteamId)
                    continue;
                if(serialized == null)
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);
                MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, p.SteamUserId);
            }
            TempPlayers.Clear();
        }

        void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer)
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(serialized);
                HandlePacket(packet, senderSteamId, serialized);
            }
            catch(Exception e) { MyLog.Default?.WriteLineAndConsole("[Config Manager] packet error: " + e); }
        }

        void HandlePacket(PacketBase packet, ulong senderSteamId, byte[] serialized = null)
        {
            if(MyAPIGateway.Session.IsServer && senderSteamId != packet.OriginalSenderSteamId)
            {
                packet.OriginalSenderSteamId = senderSteamId;
                serialized = null;
            }

            RelayMode relay = RelayMode.NoRelay;
            packet.Received(ref relay, senderSteamId);

            if(MyAPIGateway.Session.IsServer && relay == RelayMode.RelayOriginal)
                RelayToOthers(packet, serialized, senderSteamId);
        }
    }

    public enum RelayMode
    {
        NoRelay = 0,
        RelayOriginal,
    }
}
