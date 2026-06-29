using System;
using System.Collections.Generic;
using ConfigSdk.Sync;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace ConfigSdk
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class ConfigSdkSession : MySessionComponentBase
    {
        public static ConfigSdkSession Instance;

        // protocol (must match EasyConfig consumer helper)
        public const long Channel = 2730005222;
        public const int ProtocolVersion = 1;
        const int Msg_SdkReady = 1, Msg_Register = 2;

        public readonly Dictionary<string, RegisteredMod> Mods = new Dictionary<string, RegisteredMod>();

        ConfigCommands _commands;
        ConfigNetwork _network;
        ConfigMenu _menu;

        public ConfigNetwork Network => _network;
        public bool IsServer => MyAPIGateway.Multiplayer.IsServer;

        public override void LoadData()
        {
            Instance = this;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnModMessage);

            _network = new ConfigNetwork(this);
            _network.Register();

            if(!MyAPIGateway.Utilities.IsDedicated)
            {
                _commands = new ConfigCommands(this);
                _menu = new ConfigMenu(this);
            }
        }

        public override void BeforeStart()
        {
            try
            {
                // tell consumers the SDK is up so any that loaded first (re)register
                MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { Msg_SdkReady });
                _menu?.Init(); // connect to RichHud; the menu builds once the link is up
            }
            catch(Exception e) { Log.Error(e); }
        }

        protected override void UnloadData()
        {
            try
            {
                MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnModMessage);
                _commands?.Dispose();
                _network?.Unregister();
            }
            catch(Exception e) { Log.Error(e); }
            Mods.Clear();
            Instance = null;
        }

        void OnModMessage(object obj)
        {
            try
            {
                object[] arr = obj as object[];
                if(arr == null || arr.Length < 1 || !(arr[0] is int))
                    return;

                if((int)arr[0] == Msg_Register)
                    HandleRegister(arr);
            }
            catch(Exception e) { Log.Error(e); }
        }

        void HandleRegister(object[] arr)
        {
            if(arr.Length < 5) return;
            if(!(arr[1] is int) || (int)arr[1] != ProtocolVersion)
            {
                Log.Error($"Ignoring registration with protocol {arr[1]} (expected {ProtocolVersion}).");
                return;
            }

            string modId = arr[2] as string;
            object[] rawItems = arr[3] as object[];
            Action<string, object> onChanged = arr[4] as Action<string, object>;
            if(string.IsNullOrEmpty(modId) || rawItems == null || onChanged == null)
                return;

            RegisteredMod reg = new RegisteredMod(modId, onChanged);
            foreach(object o in rawItems)
            {
                object[] it = o as object[];
                if(it == null || it.Length < 7) continue;
                string key = it[0] as string;
                if(string.IsNullOrEmpty(key)) continue;
                bool restart = it.Length >= 8 && it[7] is bool && (bool)it[7];
                reg.Items[key] = new ConfigItem(key, (int)it[1], (int)it[2], it[3], (bool)it[4], (double)it[5], (double)it[6], restart);
            }

            Mods[modId] = reg;

            // load from disk (server scope from world on the server; client scope from local), persist any new keys
            ConfigStorage.Load(reg);
            ConfigStorage.Save(reg);
            reg.PushAll();

            // on a client, pull current server-scope values from the server
            if(!IsServer && reg.HasScope(ConfigScope.Server))
                _network.RequestServerConfig(modId);

            Log.Info($"Registered '{modId}' ({reg.Items.Count} items).");
        }

        // ---------- permissions ----------
        public bool IsAdmin(ulong steamId)
        {
            if(steamId == 0)
                return true; // server console / SP
            MyPromoteLevel lvl = MyAPIGateway.Session.GetUserPromoteLevel(steamId);
            return lvl == MyPromoteLevel.Admin || lvl == MyPromoteLevel.Owner;
        }

        public bool CanEdit(ConfigItem item, ulong steamId) =>
            item.Scope == ConfigScope.Client || IsAdmin(steamId);

        // ---------- lookups ----------
        public bool TryGetItem(string modId, string key, out RegisteredMod reg, out ConfigItem item)
        {
            item = null;
            return Mods.TryGetValue(modId, out reg) && reg.Items.TryGetValue(key, out item);
        }

        // ---------- set a value (entry point for chat command / menu) ----------
        // Returns false + error if not allowed / invalid. Routes server-scope edits from clients to the server.
        public bool RequestSetValue(string modId, string key, string valueText, ulong bySteamId, out string error)
        {
            error = null;
            RegisteredMod reg;
            ConfigItem item;
            if(!TryGetItem(modId, key, out reg, out item))
            {
                error = $"No config '{key}' in mod '{modId}'.";
                return false;
            }

            if(!CanEdit(item, bySteamId))
            {
                error = $"'{key}' is a server setting; only admins can change it.";
                return false;
            }

            bool parseOk;
            object parsed = item.Parse(valueText, out parseOk);
            if(!parseOk)
            {
                error = $"'{valueText}' is not a valid {CType.Name(item.Type)}" + (item.HasRange ? $" in [{item.MinString}..{item.MaxString}]." : ".");
                return false;
            }

            if(item.Scope == ConfigScope.Server && !IsServer)
            {
                // client admin -> ask the server to apply authoritatively
                _network.SendSetServerValue(modId, key, valueText);
                return true;
            }

            ApplyValue(reg, item, parsed, broadcast: item.Scope == ConfigScope.Server);
            return true;
        }

        // authoritative apply: set value, persist, and (unless restart-required) push live + broadcast
        public void ApplyValue(RegisteredMod reg, ConfigItem item, object value, bool broadcast)
        {
            string oldStr = item.CurrentString;
            item.Current = value;
            ConfigStorage.Save(reg); // always persist (loads on next world start)

            string newStr = item.CurrentString;
            if(item.RestartRequired)
                return; // persisted above; loads on next world start. Caller (menu) shows the warning.

            reg.Push(item); // apply live
            if(oldStr != newStr)
                NotifyChange(reg.ModId, item.Key, oldStr, newStr);
            if(broadcast && IsServer)
                _network.BroadcastServerConfig(reg);
        }

        // ---------- reload from disk ----------
        public void ReloadAll(ulong bySteamId)
        {
            int totalChanges = 0;
            foreach(RegisteredMod reg in Mods.Values)
                totalChanges += ReloadMod(reg);

            if(IsServer)
            {
                foreach(RegisteredMod reg in Mods.Values)
                    if(reg.HasScope(ConfigScope.Server))
                        _network.BroadcastServerConfig(reg);
                Chat($"Reloaded config from disk — {totalChanges} value(s) changed.");
            }
            else
            {
                _network.SendReloadRequest(); // ask server to reload its files + rebroadcast (admin-gated server-side)
                Chat($"Reloaded local client config — {totalChanges} value(s) changed. Requested server reload.");
            }
        }

        // server-side: reload server-scope files from disk and push to all clients (admin only)
        public void ServerReloadAndBroadcast(ulong bySteamId)
        {
            if(!IsServer || !IsAdmin(bySteamId))
                return;
            foreach(RegisteredMod reg in Mods.Values)
            {
                if(!reg.HasScope(ConfigScope.Server))
                    continue;
                ReloadMod(reg);
                _network.BroadcastServerConfig(reg);
            }
        }

        int ReloadMod(RegisteredMod reg)
        {
            List<ConfigChange> changes = ConfigStorage.Load(reg);
            foreach(ConfigChange c in changes)
            {
                ConfigItem item;
                bool restart = reg.Items.TryGetValue(c.Key, out item) && item.RestartRequired;
                if(!restart && item != null)
                {
                    reg.Push(item); // apply live
                    NotifyChange(reg.ModId, c.Key, c.OldValue, c.NewValue);
                }
                else
                {
                    Chat($"{reg.ModId}.{c.Key}: {c.OldValue} -> {c.NewValue} — restart the world to apply.");
                }
            }
            return changes.Count;
        }

        // ---------- chat helpers ----------
        public void NotifyChange(string modId, string key, string oldVal, string newVal)
        {
            Chat($"{modId}.{key}: {oldVal} -> {newVal}");
        }

        public void Chat(string msg)
        {
            Log.Info(msg);
            if(!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowMessage("Config Manager", msg);
        }
    }
}
