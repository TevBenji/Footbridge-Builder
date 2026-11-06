// Placemaker — UI bindings, exactly as UI/src/contract.ts defines them (group "Placemaker").
//
// Each value is a ValueBinding<string> carrying JSON (never string[] — that broke mod load in
// PLT; RESEARCH-NOTES rule 22). The UI parses it (JSON.parse). The value is rebuilt only when
// its inputs changed (version stamps) and pushed only when the JSON actually differs
// (ValueBinding compares) — not ~100 bindings per frame like GRG.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one Footbridge Builder state value and the tool's open and close triggers.
using System;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using FootbridgeBuilder.Tool;
using Game.UI;
using UnityEngine.Scripting;

namespace FootbridgeBuilder.Panel
{
    public sealed partial class PanelSystem : UISystemBase
    {
        private const string Group = "FootbridgeBuilder";
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
            }
            catch (Exception exception)
            {
                Enabled = false;
                Mod.Logger.Error(exception, "Footbridge Builder: panel initialization failed");
            }
        }

        private void Trigger(string name, Action action) =>
            AddBinding(new TriggerBinding(Group, name, () => Safe(name, action)));

        private static void Safe(string name, Action action)
        {
            try { action(); }
            catch (Exception exception) { Mod.Logger.Error(exception, "Footbridge Builder: panel trigger '" + name + "' failed"); }
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
                _state.Update(StateJson(state));
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

        private static string StateJson(ToolState state)
        {
            var json = new StringBuilder("{\"stage\":");
            String(json, state.Stage.ToString());
            json.Append(",\"summary\":"); String(json, state.Summary);
            json.Append(",\"messages\":[");
            var messages = state.Messages ?? Array.Empty<string>();
            for (int index = 0; index < messages.Length; index++)
            {
                if (index > 0) json.Append(',');
                String(json, messages[index]);
            }
            json.Append("],\"progress\":").Append(Number(state.Progress));
            return json.Append('}').ToString();
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
