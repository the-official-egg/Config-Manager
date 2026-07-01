using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConfigSdk
{
    public enum ConfigScope { Client = 0, Server = 1 }

    public static class CType // type codes; MUST match EasyConfig (consumer helper)
    {
        public const int Bool = 0, Int = 1, Float = 2, Double = 3, String = 4;

        public static string Name(int t)
        {
            switch(t)
            {
                case Bool: return "bool";
                case Int: return "int";
                case Float: return "float";
                case Double: return "double";
                default: return "string";
            }
        }
    }

    // One configurable value, server- or client-scoped.
    public class ConfigItem
    {
        public readonly string Key;
        public readonly int Type;
        public readonly ConfigScope Scope;
        public readonly object Default;
        public readonly bool HasRange;
        public readonly double Min;
        public readonly double Max;
        public readonly bool RestartRequired; // value change only takes effect after a world reload

        public object Current;

        public ConfigItem(string key, int type, int scope, object def, bool hasRange, double min, double max, bool restartRequired)
        {
            Key = key;
            Type = type;
            Scope = (scope == (int)ConfigScope.Server) ? ConfigScope.Server : ConfigScope.Client;
            Default = def;
            HasRange = hasRange;
            Min = min;
            Max = max;
            RestartRequired = restartRequired;
            Current = def;
        }

        public string CurrentString => Format(Current);
        public string DefaultString => Format(Default);
        public string MinString => HasRange ? Min.ToString(CultureInfo.InvariantCulture) : null;
        public string MaxString => HasRange ? Max.ToString(CultureInfo.InvariantCulture) : null;

        public string Format(object value)
        {
            if(value == null)
                return "";

            switch(Type)
            {
                case CType.Bool: return ((bool)value) ? "true" : "false";
                case CType.Int: return ((int)value).ToString(CultureInfo.InvariantCulture);
                case CType.Float: return ((float)value).ToString("R", CultureInfo.InvariantCulture);
                case CType.Double: return ((double)value).ToString("R", CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        /// <summary>Parse a string to this item's type, clamp to range, fall back to Default on failure.</summary>
        public object Parse(string text, out bool ok)
        {
            ok = false;
            if(text == null)
                return Default;

            text = text.Trim();
            try
            {
                switch(Type)
                {
                    case CType.Bool:
                    {
                        bool b;
                        if(bool.TryParse(text, out b)) { ok = true; return b; }
                        if(text == "1") { ok = true; return true; }
                        if(text == "0") { ok = true; return false; }
                        return Default;
                    }
                    case CType.Int:
                    {
                        int i;
                        if(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                        { ok = true; return ClampInt(i); }
                        return Default;
                    }
                    case CType.Float:
                    {
                        float f;
                        if(float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        { ok = true; return (float)ClampD(f); }
                        return Default;
                    }
                    case CType.Double:
                    {
                        double d;
                        if(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        { ok = true; return ClampD(d); }
                        return Default;
                    }
                    default:
                        ok = true;
                        return text;
                }
            }
            catch
            {
                return Default;
            }
        }

        int ClampInt(int v)
        {
            if(!HasRange) return v;
            if(v < Min) return (int)Min;
            if(v > Max) return (int)Max;
            return v;
        }

        double ClampD(double v)
        {
            if(!HasRange) return v;
            if(v < Min) return Min;
            if(v > Max) return Max;
            return v;
        }
    }

    // All config items registered by one consumer mod, plus its value-update callback.
    public class RegisteredMod
    {
        public readonly string ModId;
        public readonly Action<string, object> OnChanged; // push values back to the consumer
        public readonly Dictionary<string, ConfigItem> Items = new Dictionary<string, ConfigItem>();

        public RegisteredMod(string modId, Action<string, object> onChanged)
        {
            ModId = modId;
            OnChanged = onChanged;
        }

        public bool HasScope(ConfigScope scope)
        {
            foreach(ConfigItem it in Items.Values)
                if(it.Scope == scope)
                    return true;
            return false;
        }

        public void Push(ConfigItem item)
        {
            OnChanged?.Invoke(item.Key, item.Current);
        }

        public void PushAll()
        {
            foreach(ConfigItem it in Items.Values)
                OnChanged?.Invoke(it.Key, it.Current);
        }
    }
}
