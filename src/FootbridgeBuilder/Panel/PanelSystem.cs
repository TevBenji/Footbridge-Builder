// Placemaker — UI bindings, exactly as UI/src/contract.ts defines them (group "Placemaker").
//
// Each value is a ValueBinding<string> carrying JSON (never string[] — that broke mod load in
// PLT; RESEARCH-NOTES rule 22). The UI parses it (JSON.parse). The value is rebuilt only when
// its inputs changed (version stamps) and pushed only when the JSON actually differs
// (ValueBinding compares) — not ~100 bindings per frame like GRG.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one Footbridge Builder state value, the bridge on show and its settings, and clamped setting triggers.
using System;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using FootbridgeBuilder.Core;
using FootbridgeBuilder.Settings;
using FootbridgeBuilder.Tool;
using Game.UI;
using UnityEngine.Scripting;

namespace FootbridgeBuilder.Panel
{
    public sealed partial class PanelSystem : UISystemBase
    {
        private const string Group = "FootbridgeBuilder";
        private const string BugLink = "https://github.com/TevBenji/Footbridge-Builder/issues/new";
        // Empty hides the panel's Discord buttons.
        private const string DiscordLink = "https://discord.com/channels/263634513861541888/1568265920902594560";
        private FootbridgeToolSystem _tool;
        private ValueBinding<string> _state;
        private bool _published, _updateFailed;
        private int _version;

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                _tool = World.GetOrCreateSystemManaged<FootbridgeToolSystem>();
                AddBinding(_state = new ValueBinding<string>(Group, "state", "{}"));
                Trigger("toggleTool", () => _tool.Toggle());
                Trigger("close", () => _tool.Close());
                Trigger("build", () => _tool.Build());
                Trigger<string>("setDesign", value => ChangeSettings(settings =>
                {
                    if (!Enum.TryParse(value, out Design design) || !Enum.IsDefined(typeof(Design), design)) return false;
                    settings.Design = design.ToString();
                    return true;
                }));
                Trigger<string>("setPath", value => ChangeSettings(settings =>
                {
                    if (Array.IndexOf(_tool.State.PathNames, value) < 0) return false;
                    settings.PathName = value;
                    return true;
                }));
                Trigger<bool>("setRemoveCrossings", value => ChangeSettings(settings => { settings.RemoveCrossings = value; return true; }));
                NumberTrigger("setReach", 20, 150, (settings, value) => settings.CrossingReach = (int)Math.Round(value));
                // Below half a metre the game refused the deck in the test city (its own rounding adds to the road's and path's heights).
                NumberTrigger("setExtraClearance", 0.5, 3, (settings, value) => settings.ExtraClearance = (float)(Math.Round(value * 4) / 4));
                Trigger<double>("setTurn", value =>
                {
                    if (!double.IsNaN(value) && !double.IsInfinity(value)) _tool.SetTurn((float)Math.Max(-30, Math.Min(30, value)));
                });
                AddBinding(new TriggerBinding<double, double>(Group, "setPanelPlace", (x, y) => Safe("setPanelPlace", () => SavePanel(settings =>
                {
                    if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) return;
                    bool usual = x < 0 || y < 0;
                    settings.PanelX = usual ? -1f : (float)Math.Min(1, x);
                    settings.PanelY = usual ? -1f : (float)Math.Min(1, y);
                }))));
                Trigger<bool>("setPanelCollapsed", value => SavePanel(settings => settings.PanelCollapsed = value));
                // A key, never an address, so the panel can only open these two.
                Trigger<string>("openLink", key =>
                {
                    string link = key == "bug" ? BugLink : key == "discord" ? DiscordLink : "";
                    if (link.Length > 0) UnityEngine.Application.OpenURL(link);
                });
            }
            catch (Exception exception)
            {
                Enabled = false;
                Mod.Logger.Error(exception, "Footbridge Builder: panel initialization failed");
            }
        }

        private void Trigger(string name, Action action) =>
            AddBinding(new TriggerBinding(Group, name, () => Safe(name, action)));

        private void Trigger<T>(string name, Action<T> action) =>
            AddBinding(new TriggerBinding<T>(Group, name, value => Safe(name, () => action(value))));

        private static void Safe(string name, Action action)
        {
            try { action(); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Footbridge Builder: panel trigger '" + name + "' failed"); }
        }

        private void NumberTrigger(string name, double minimum, double maximum, Action<FootbridgeSettings, double> write) =>
            Trigger<double>(name, value => ChangeSettings(settings =>
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return false;
                write(settings, Math.Max(minimum, Math.Min(maximum, value)));
                return true;
            }));

        private void ChangeSettings(Func<FootbridgeSettings, bool> write)
        {
            var settings = Mod.Settings;
            if (settings == null || _tool.State.Stage == ToolStage.Building || !write(settings)) return;
            // Replanning still happens if saving fails, so the panel never leaves a stale bridge buildable.
            try { settings.ApplyAndSave(); }
            finally { _tool.SettingsChanged(); }
        }

        // Moving or folding the panel doesn't change the bridge, so nothing is planned again.
        private void SavePanel(Action<FootbridgeSettings> write)
        {
            var settings = Mod.Settings;
            if (settings == null) return;
            write(settings);
            try { settings.ApplyAndSave(); }
            finally { _tool.State.Version++; }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                base.OnUpdate();
                if (_state == null || _tool == null) return;
                var state = _tool.State;
                if (_published && _version == state.Version) return;
                _state.Update(StateJson(state, Mod.Settings));
                _version = state.Version;
                _published = true;
                _updateFailed = false;
            }
            catch (Exception exception)
            {
                // A repeated failure must remain visible in the log without flooding it every frame.
                if (!_updateFailed) Mod.Logger.Error(exception, "Footbridge Builder: panel update failed");
                _updateFailed = true;
            }
        }

        private static string StateJson(ToolState state, FootbridgeSettings settings)
        {
            var json = new StringBuilder("{\"stage\":");
            String(json, state.Stage.ToString());
            json.Append(",\"summary\":"); String(json, state.Summary);
            json.Append(",\"messages\":"); Strings(json, state.Messages);
            json.Append(",\"progress\":").Append(Number(state.Progress));
            json.Append(",\"buildable\":").Append(state.Buildable ? "true" : "false");
            json.Append(",\"designName\":"); String(json, state.DesignName);
            json.Append(",\"deckHeight\":").Append(Number(state.DeckHeight));
            json.Append(",\"useLabel\":"); String(json, state.UseLabel);
            json.Append(",\"useShare\":").Append(Number(state.UseShare));
            json.Append(",\"extraWalk\":").Append(Number(state.ExtraWalk));
            json.Append(",\"cost\":").Append(state.Cost.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"crossingsRemoved\":").Append(state.CrossingsRemoved.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"competingMarked\":").Append(state.CompetingMarked ? "true" : "false");
            json.Append(",\"competingDistance\":").Append(((int)Math.Round(state.CompetingDistance)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.Append(",\"buildingsReplaced\":").Append(state.BuildingsReplaced.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"refused\":").Append(state.Refused.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"turn\":").Append(Number(state.Turn));
            json.Append(",\"pinned\":").Append(state.Pinned ? "true" : "false");
            json.Append(",\"discord\":").Append(DiscordLink.Length > 0 ? "true" : "false");
            json.Append(",\"paths\":[");
            for (int index = 0; index < state.PathNames.Length; index++)
            {
                if (index > 0) json.Append(',');
                json.Append("{\"name\":"); String(json, state.PathNames[index]);
                json.Append(",\"label\":"); String(json, index < state.PathLabels.Length ? state.PathLabels[index] : state.PathNames[index]);
                json.Append('}');
            }
            json.Append("],\"settings\":{\"design\":"); String(json, settings?.Design ?? "Auto");
            json.Append(",\"path\":"); String(json, settings?.PathName ?? "");
            json.Append(",\"removeCrossings\":").Append(settings == null || settings.RemoveCrossings ? "true" : "false");
            json.Append(",\"reach\":").Append(Number(settings?.CrossingReach ?? 60));
            json.Append(",\"extraClearance\":").Append(Number(settings?.ExtraClearance ?? 0.5f));
            json.Append(",\"panelX\":").Append(Number(settings?.PanelX ?? -1f));
            json.Append(",\"panelY\":").Append(Number(settings?.PanelY ?? -1f));
            json.Append(",\"panelCollapsed\":").Append(settings != null && settings.PanelCollapsed ? "true" : "false");
            return json.Append("}}").ToString();
        }

        private static void Strings(StringBuilder json, string[] values)
        {
            json.Append('[');
            var list = values ?? Array.Empty<string>();
            for (int index = 0; index < list.Length; index++)
            {
                if (index > 0) json.Append(',');
                String(json, list[index]);
            }
            json.Append(']');
        }

        private static string Number(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? "null" : value.ToString("R", CultureInfo.InvariantCulture);

        private static void String(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char character in value ?? "")
            {
                switch (character)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    default:
                        if (character < 32 || char.IsSurrogate(character) || character == '\u2028' || character == '\u2029')
                            json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else json.Append(character);
                        break;
                }
            }
            json.Append('"');
        }
    }
}
