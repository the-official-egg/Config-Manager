using System;
using System.Collections.Generic;
using System.Globalization;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using Sandbox.ModAPI;

namespace ConfigSdk
{
    // Builds the Rich HUD terminal pages (one per mod); edits route through ConfigSdkSession.RequestSetValue.
    public class ConfigMenu
    {
        readonly ConfigSdkSession _s;
        bool _initStarted, _pagesBuilt;

        public ConfigMenu(ConfigSdkSession session) { _s = session; }

        ulong Me => MyAPIGateway.Session?.Player?.SteamUserId ?? 0;

        public void Init()
        {
            if(_initStarted || MyAPIGateway.Utilities.IsDedicated)
                return;
            _initStarted = true;
            try
            {
                // connects to RichHudMaster; BuildPages runs once the link is up (a few frames later)
                RichHudClient.Init("Config Manager", BuildPages, OnReset);
            }
            catch(Exception e)
            {
                Log.Error("RichHud init failed (is RichHudMaster installed?): " + e);
            }
        }

        void OnReset() { _pagesBuilt = false; }

        void BuildPages()
        {
            try
            {
                if(_pagesBuilt)
                    return;
                _pagesBuilt = true;

                bool admin = _s.IsAdmin(Me);
                List<IModRootMember> pages = new List<IModRootMember>();
                foreach(RegisteredMod reg in _s.Mods.Values)
                    pages.Add(BuildPage(reg, admin));

                if(pages.Count > 0)
                {
                    RichHudTerminal.Root.AddRange(pages.ToArray());
                    RichHudTerminal.Root.Enabled = true;
                }
            }
            catch(Exception e) { Log.Error(e); }
        }

        // RichHud stacks categories vertically but tiles horizontally and clamps tile width, so each 3-control chunk becomes its own single-tile category to force one vertical column.
        const int ControlsPerTile = 3;

        ControlPage BuildPage(RegisteredMod reg, bool admin)
        {
            ControlPage page = new ControlPage() { Name = reg.ModId };

            List<TerminalControlBase> serverControls = new List<TerminalControlBase>();
            List<TerminalControlBase> clientControls = new List<TerminalControlBase>();

            foreach(ConfigItem item in reg.Items.Values)
            {
                bool editable = (item.Scope == ConfigScope.Client) || admin;
                TerminalControlBase ctrl = BuildControl(reg, item, editable);
                if(ctrl == null)
                    continue;
                if(item.Scope == ConfigScope.Server) serverControls.Add(ctrl);
                else clientControls.Add(ctrl);
            }

            AddScope(page, "Server settings", admin ? "Applies to everyone (admin)" : "Applies to everyone (admin-only)", serverControls);
            AddScope(page, "Client settings", "Your local settings", clientControls);

            // reload + reset buttons in their own row at the bottom
            ControlTile actionsTile = new ControlTile();
            TerminalButton reloadBtn = new TerminalButton() { Name = "Reload from file" };
            reloadBtn.ControlChangedHandler = (s, a) => _s.ReloadAll(Me);
            actionsTile.Add(reloadBtn);
            TerminalButton resetBtn = new TerminalButton() { Name = "Reset to defaults" };
            resetBtn.ControlChangedHandler = (s, a) => _s.ResetAll(Me);
            actionsTile.Add(resetBtn);
            ControlCategory actionsCat = new ControlCategory();
            actionsCat.HeaderText = "";
            actionsCat.SubheaderText = "";
            actionsCat.TileContainer.Add(actionsTile);
            page.CategoryContainer.Add(actionsCat);

            return page;
        }

        void AddScope(ControlPage page, string header, string sub, List<TerminalControlBase> controls)
        {
            for(int i = 0; i < controls.Count; i += ControlsPerTile)
            {
                ControlTile tile = new ControlTile();
                for(int j = i; j < controls.Count && j < i + ControlsPerTile; j++)
                    tile.Add(controls[j]);

                ControlCategory cat = new ControlCategory();
                cat.HeaderText = (i == 0) ? header : "";       // header only on the first chunk
                cat.SubheaderText = (i == 0) ? sub : "";
                cat.TileContainer.Add(tile);
                page.CategoryContainer.Add(cat);
            }
        }

        TerminalControlBase BuildControl(RegisteredMod reg, ConfigItem item, bool editable)
        {
            switch(item.Type)
            {
                case CType.Bool:
                {
                    TerminalCheckbox cb = new TerminalCheckbox() { Name = DisplayName(item), Value = (bool)item.Current };
                    cb.CustomValueGetter = () => (bool)item.Current;
                    cb.ControlChangedHandler = (s, a) =>
                    {
                        if(editable) Set(reg, item, ((TerminalCheckbox)s).Value ? "true" : "false");
                    };
                    return cb;
                }

                case CType.Int:
                case CType.Float:
                case CType.Double:
                {
                    if(!item.HasRange) // no range -> text field
                        return TextField(reg, item, editable);

                    TerminalSlider sl = new TerminalSlider()
                    {
                        Name = DisplayName(item),
                        Min = (float)item.Min,
                        Max = (float)item.Max,
                        Value = ToFloat(item.Current),
                        ValueText = item.CurrentString,
                    };
                    sl.CustomValueGetter = () => ToFloat(item.Current);
                    sl.ControlChangedHandler = (s, a) =>
                    {
                        TerminalSlider slider = (TerminalSlider)s;
                        string text = (item.Type == CType.Int)
                            ? ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture)
                            : slider.Value.ToString("0.###", CultureInfo.InvariantCulture);
                        slider.ValueText = text;
                        if(editable) Set(reg, item, text);
                    };
                    return sl;
                }

                default:
                    return TextField(reg, item, editable);
            }
        }

        TerminalTextField TextField(RegisteredMod reg, ConfigItem item, bool editable)
        {
            TerminalTextField tf = new TerminalTextField() { Name = DisplayName(item), Value = item.CurrentString };
            tf.CustomValueGetter = () => item.CurrentString;
            tf.ControlChangedHandler = (s, a) =>
            {
                if(editable) Set(reg, item, ((TerminalTextField)s).Value);
            };
            return tf;
        }

        // restart-required settings are flagged in their label
        static string DisplayName(ConfigItem item) => item.RestartRequired ? item.Key + " (restart)" : item.Key;

        // single entry point for menu edits: applies via the SDK, then warns if a restart is needed
        void Set(RegisteredMod reg, ConfigItem item, string text)
        {
            // ignore no-op writes: Rich HUD fires ControlChanged on init/refresh, not just on user edits
            bool parseOk;
            object parsed = item.Parse(text, out parseOk);
            if(parseOk && item.Format(parsed) == item.CurrentString)
                return;

            string err;
            if(_s.RequestSetValue(reg.ModId, item.Key, text, Me, out err))
            {
                if(item.RestartRequired)
                    _s.Chat($"{reg.ModId}.{item.Key} changed - restart the world to apply.");
            }
            else if(!string.IsNullOrEmpty(err))
            {
                _s.Chat("Config: " + err);
            }
        }

        static float ToFloat(object o)
        {
            if(o is float) return (float)o;
            if(o is int) return (int)o;
            if(o is double) return (float)(double)o;
            return 0f;
        }
    }
}
