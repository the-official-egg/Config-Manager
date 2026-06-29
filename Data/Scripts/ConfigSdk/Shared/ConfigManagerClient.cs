using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

// ConfigManagerClient - drop-in helper for the "Config Manager" mod (talks to it over ModMessage).
// Not installed? Every value just stays at its Default, so your mod still works. Usage:
//   static readonly ModConfig Cfg = new ModConfig("MyMod");
//   public static readonly ConfigValue<float> Strength = Cfg.Float("Strength").Default(8).Min(0).Max(100).Scope(ConfigScope.Server);
//   Cfg.Register() in LoadData  ·  float s = Strength.Value  ·  Cfg.Unregister() in UnloadData
// Scope: Client = per-player local file · Server = world save, synced, admin-only edits.
// RestartRequired() = change applies only after a world reload (the menu warns). Default false.
namespace ConfigManagerClient
{
    public enum ConfigScope { Client = 0, Server = 1 }

    public class ConfigValue<T>
    {
        public T Value { get; internal set; }
        internal ConfigValue(T def) { Value = def; }
        public override string ToString() => Value?.ToString() ?? "null";
    }

    public class ModConfig
    {
        const long Channel = 2730005222;  // must match the Config Manager mod
        const int Ver = 1, Msg_Ready = 1, Msg_Register = 2;

        internal class Entry
        {
            public string Key; public int Type; public int Scope; public object Default;
            public bool HasRange; public double Min, Max; public bool Restart;
            public Action<object> Apply;
        }

        readonly string _id;
        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>();
        bool _on;

        public ModConfig(string modId)
        {
            if(string.IsNullOrEmpty(modId)) throw new ArgumentException("modId required");
            _id = modId;
        }

        public ConfigBuilder<bool> Bool(string k) => Make<bool>(k, 0);
        public ConfigBuilder<int> Int(string k) => Make<int>(k, 1);
        public ConfigBuilder<float> Float(string k) => Make<float>(k, 2);
        public ConfigBuilder<double> Double(string k) => Make<double>(k, 3);
        public ConfigBuilder<string> String(string k) => Make<string>(k, 4);

        ConfigBuilder<T> Make<T>(string key, int type)
        {
            if(_on) throw new InvalidOperationException("declare values before Register()");
            var v = new ConfigValue<T>(default(T));
            var e = new Entry { Key = key, Type = type, Default = default(T), Apply = o => { if(o is T) v.Value = (T)o; } };
            _entries.Add(e); _byKey[key] = e;
            return new ConfigBuilder<T>(e, v);
        }

        public void Register()
        {
            if(_on) return;
            _on = true;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, OnMsg);
            Send();  // in case Config Manager is already loaded
        }

        public void Unregister()
        {
            if(!_on) return;
            _on = false;
            MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, OnMsg);
        }

        void OnMsg(object o)  // Config Manager (re)loaded -> resend our registration
        {
            var a = o as object[];
            if(a != null && a.Length >= 1 && a[0] is int && (int)a[0] == Msg_Ready) Send();
        }

        void Send()
        {
            var items = new object[_entries.Count];
            for(int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                items[i] = new object[] { e.Key, e.Type, e.Scope, e.Default, e.HasRange, e.Min, e.Max, e.Restart };
            }
            object cb = new Action<string, object>((key, val) => { Entry e; if(key != null && _byKey.TryGetValue(key, out e)) e.Apply(val); });
            MyAPIGateway.Utilities.SendModMessage(Channel, new object[] { Msg_Register, Ver, _id, items, cb });
        }
    }

    public class ConfigBuilder<T>
    {
        readonly ModConfig.Entry _e; readonly ConfigValue<T> _v;
        internal ConfigBuilder(ModConfig.Entry e, ConfigValue<T> v) { _e = e; _v = v; }
        public ConfigBuilder<T> Default(T x) { _e.Default = x; _v.Value = x; return this; }
        public ConfigBuilder<T> Min(double x) { _e.Min = x; _e.HasRange = true; return this; }
        public ConfigBuilder<T> Max(double x) { _e.Max = x; _e.HasRange = true; return this; }
        public ConfigBuilder<T> Scope(ConfigScope s) { _e.Scope = (int)s; return this; }
        public ConfigBuilder<T> RestartRequired(bool v = true) { _e.Restart = v; return this; }
        public ConfigValue<T> Value => _v;
        public static implicit operator ConfigValue<T>(ConfigBuilder<T> b) => b._v;
    }
}
