using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Sandbox.ModAPI;

namespace ConfigSdk
{
    // XML file model: one file per mod; server holds both Server and Client sections in the world save, clients keep a local Client-only file (server values arrive over the network).
    [XmlRoot("ModConfig")]
    public class ConfigFileData
    {
        [XmlAttribute] public string Mod;
        [XmlArray("Server"), XmlArrayItem("Entry")] public List<ConfigEntryData> Server = new List<ConfigEntryData>();
        [XmlArray("Client"), XmlArrayItem("Entry")] public List<ConfigEntryData> Client = new List<ConfigEntryData>();
    }

    public class ConfigEntryData
    {
        [XmlAttribute] public string Key;
        [XmlAttribute] public string Type;     // hint only
        [XmlAttribute] public string Default;  // hint only
        [XmlAttribute] public string Min;      // hint only
        [XmlAttribute] public string Max;      // hint only
        [XmlText] public string Value;
    }

    public struct ConfigChange
    {
        public string Key;
        public string OldValue;
        public string NewValue;
    }

    public static class ConfigStorage
    {
        static string FileName(string modId) => modId + ".xml";

        /// <summary>Read this mod's file and apply values. Returns the changes made (for logging).</summary>
        public static List<ConfigChange> Load(RegisteredMod reg)
        {
            List<ConfigChange> changes = new List<ConfigChange>();

            if(MyAPIGateway.Multiplayer.IsServer)
            {
                ConfigFileData data = ReadWorld(FileName(reg.ModId));
                Apply(reg, data, ConfigScope.Server, changes);
                Apply(reg, data, ConfigScope.Client, changes);
            }
            else
            {
                // client: only local client-scope values; server values arrive via the network
                ConfigFileData data = ReadLocal(FileName(reg.ModId));
                Apply(reg, data, ConfigScope.Client, changes);
            }

            return changes;
        }

        /// <summary>Persist current values (adds missing keys with defaults, refreshes hints).</summary>
        public static void Save(RegisteredMod reg)
        {
            bool server = MyAPIGateway.Multiplayer.IsServer;
            ConfigFileData data = new ConfigFileData { Mod = reg.ModId };

            foreach(ConfigItem item in reg.Items.Values)
            {
                ConfigEntryData entry = Entry(item);
                if(item.Scope == ConfigScope.Server)
                {
                    if(server) data.Server.Add(entry); // only the server owns server values
                }
                else
                {
                    data.Client.Add(entry);
                }
            }

            if(server)
                WriteWorld(FileName(reg.ModId), data);          // one world file, both sections
            else if(data.Client.Count > 0)
                WriteLocal(FileName(reg.ModId), data);          // client: local file, client section only
        }

        static void Apply(RegisteredMod reg, ConfigFileData data, ConfigScope scope, List<ConfigChange> changes)
        {
            List<ConfigEntryData> entries = (data == null) ? null : (scope == ConfigScope.Server ? data.Server : data.Client);
            if(entries == null)
                return;

            foreach(ConfigEntryData entry in entries)
            {
                ConfigItem item;
                if(entry.Key == null || !reg.Items.TryGetValue(entry.Key, out item) || item.Scope != scope)
                    continue;

                string oldStr = item.CurrentString;
                bool ok;
                item.Current = item.Parse(entry.Value, out ok);
                string newStr = item.CurrentString;
                if(oldStr != newStr)
                    changes.Add(new ConfigChange { Key = item.Key, OldValue = oldStr, NewValue = newStr });
            }
        }

        static ConfigEntryData Entry(ConfigItem item) => new ConfigEntryData
        {
            Key = item.Key,
            Type = CType.Name(item.Type),
            Default = item.DefaultString,
            Min = item.MinString,
            Max = item.MaxString,
            Value = item.CurrentString,
        };

        // Low-level IO
        static ConfigFileData ReadWorld(string file)
        {
            try
            {
                if(!MyAPIGateway.Utilities.FileExistsInWorldStorage(file, typeof(ConfigStorage)))
                    return null;
                using(TextReader r = MyAPIGateway.Utilities.ReadFileInWorldStorage(file, typeof(ConfigStorage)))
                    return Deserialize(r.ReadToEnd());
            }
            catch(Exception e) { Log.Error(e); return null; }
        }

        static ConfigFileData ReadLocal(string file)
        {
            try
            {
                if(!MyAPIGateway.Utilities.FileExistsInLocalStorage(file, typeof(ConfigStorage)))
                    return null;
                using(TextReader r = MyAPIGateway.Utilities.ReadFileInLocalStorage(file, typeof(ConfigStorage)))
                    return Deserialize(r.ReadToEnd());
            }
            catch(Exception e) { Log.Error(e); return null; }
        }

        static void WriteWorld(string file, ConfigFileData data)
        {
            try
            {
                string xml = MyAPIGateway.Utilities.SerializeToXML(data);
                using(TextWriter w = MyAPIGateway.Utilities.WriteFileInWorldStorage(file, typeof(ConfigStorage)))
                    w.Write(xml);
            }
            catch(Exception e) { Log.Error(e); }
        }

        static void WriteLocal(string file, ConfigFileData data)
        {
            try
            {
                string xml = MyAPIGateway.Utilities.SerializeToXML(data);
                using(TextWriter w = MyAPIGateway.Utilities.WriteFileInLocalStorage(file, typeof(ConfigStorage)))
                    w.Write(xml);
            }
            catch(Exception e) { Log.Error(e); }
        }

        static ConfigFileData Deserialize(string xml)
        {
            if(string.IsNullOrWhiteSpace(xml))
                return null;
            try { return MyAPIGateway.Utilities.SerializeFromXML<ConfigFileData>(xml); }
            catch(Exception e) { Log.Error("Malformed config XML, ignoring: " + e.Message); return null; }
        }
    }
}
