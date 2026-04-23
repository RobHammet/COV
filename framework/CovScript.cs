// CovScript.cs — parser for .covscript DSL files.
// Converts the COV adventure-game script language to the same JsonObject
// structure consumed by ScriptParser / scene_script (execution pipeline unchanged).
//
// SCENE FILE STRUCTURE
//   thing name:          — defines interaction handlers for a thing node
//     on use:            — verb handler (use / look / talk / walk)
//     on walk, use:      — multi-verb handler (stored as "walk|use" in JSON)
//     on use_item axe:   — item-use handler
//   area name:           — defines area-trigger handlers (same structure as thing)
//     on move_into:
//   sequence name:       — named reusable action block
//   on entry:            — runs once when the scene is entered
//   on arrive_from theporch: — runs when arriving from a named scene
//
// DIALOG FILE STRUCTURE (game/data/dialogs/*.covscript)
//   start NodeName
//   node NodeName:
//     ...actions...
//     goto OtherNode
//   node Main:
//     choices:
//       "LABEL" -> NodeName
//       "LABEL" -> NodeName  if_flag:someFlag
//       "LABEL" -> NodeName  if_not_flag:someFlag

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

public static class CovScript
{
    private readonly struct Line
    {
        public readonly int    Indent;
        public readonly string Text;
        public readonly int    Number;
        public Line(int indent, string text, int number)
        { Indent = indent; Text = text; Number = number; }
    }

    private static readonly HashSet<string> Directions =
        new(StringComparer.OrdinalIgnoreCase) { "up", "down", "left", "right" };

    // ── Public API ────────────────────────────────────────────────────────────

    // Auto-detects scene vs dialog format and parses accordingly.
    public static JsonObject Parse(string source)
    {
        var lines = Tokenize(source);
        // Dialog files contain "start" or "node" at the top level.
        foreach (var l in lines)
            if (l.Indent == 0 && (l.Text.StartsWith("start ") || l.Text.StartsWith("node ")))
                return ParseDialog(lines);
        return ParseScene(lines);
    }

    // ── Tokenizer ─────────────────────────────────────────────────────────────

    private static List<Line> Tokenize(string source)
    {
        var result = new List<Line>();
        var raw = source.Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            string r = raw[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(r)) continue;
            string t = r.TrimStart();
            if (t.StartsWith("#")) continue;
            result.Add(new Line(r.Length - t.Length, t, i + 1));
        }
        return result;
    }

    // ── Scene parser ──────────────────────────────────────────────────────────

    private static JsonObject ParseScene(List<Line> lines)
    {
        var result = new JsonObject();
        int pos = 0;
        while (pos < lines.Count)
        {
            var line = lines[pos];
            if (line.Indent != 0) { pos++; continue; }
            string t = line.Text;

            if (t.StartsWith("thing ") && t.EndsWith(":"))
            {
                string name = t[6..^1].Trim();
                pos++;
                int childIndent = PeekIndent(lines, pos);
                EnsureObj(result, "things")[name] = ParseHandlerMap(lines, ref pos, childIndent);
            }
            else if (t.StartsWith("area ") && t.EndsWith(":"))
            {
                string name = t[5..^1].Trim();
                pos++;
                int childIndent = PeekIndent(lines, pos);
                EnsureObj(result, "areas")[name] = ParseHandlerMap(lines, ref pos, childIndent);
            }
            else if (t.StartsWith("sequence ") && t.EndsWith(":"))
            {
                string name = t[9..^1].Trim();
                pos++;
                int childIndent = PeekIndent(lines, pos);
                EnsureObj(result, "sequences")[name] = ParseActionList(lines, ref pos, childIndent);
            }
            else if (t == "on entry:")
            {
                pos++;
                int childIndent = PeekIndent(lines, pos);
                result["entry"] = ParseActionList(lines, ref pos, childIndent);
            }
            else if (t.StartsWith("on arrive_from ") && t.EndsWith(":"))
            {
                string src = t[15..^1].Trim();
                pos++;
                int childIndent = PeekIndent(lines, pos);
                EnsureObj(result, "on_arrive_from")[src] = ParseActionList(lines, ref pos, childIndent);
            }
            else if (t.StartsWith("border_style:"))
            {
                result["border_style"] = t[13..].Trim();
                pos++;
            }
            else if (t.StartsWith("border_width:"))
            {
                if (float.TryParse(t[13..].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float bw))
                    result["border_width"] = bw;
                pos++;
            }
            else
            {
                pos++;
            }
        }
        return result;
    }

    // ── Dialog parser ─────────────────────────────────────────────────────────

    private static JsonObject ParseDialog(List<Line> lines)
    {
        var result  = new JsonObject();
        var nodes   = new JsonObject();
        result["nodes"] = nodes;
        int pos = 0;
        while (pos < lines.Count)
        {
            var line = lines[pos];
            if (line.Indent != 0) { pos++; continue; }
            string t = line.Text;

            if (t.StartsWith("start "))
            {
                result["start"] = t[6..].Trim();
                pos++;
            }
            else if (t.StartsWith("node ") && t.EndsWith(":"))
            {
                string name = t[5..^1].Trim();
                pos++;
                int childIndent = PeekIndent(lines, pos);
                nodes[name] = ParseDialogNodeBody(lines, ref pos, childIndent);
            }
            else
            {
                pos++;
            }
        }
        return result;
    }

    private static JsonArray ParseDialogNodeBody(List<Line> lines, ref int pos, int myIndent)
    {
        var result = new JsonArray();
        while (pos < lines.Count && lines[pos].Indent >= myIndent)
        {
            var line = lines[pos];
            if (line.Indent != myIndent) { pos++; continue; }
            string t = line.Text;

            if (t == "choices:")
            {
                pos++;
                int choiceIndent = PeekIndent(lines, pos);
                result.Add(ParseChoices(lines, ref pos, choiceIndent));
            }
            else if (t.StartsWith("goto "))
            {
                result.Add(new JsonObject { ["action"] = "call", ["node"] = t[5..].Trim() });
                pos++;
            }
            else if (t.StartsWith("if ") && t.EndsWith(":"))
            {
                pos++;
                ParseIfBlock(lines, ref pos, myIndent, t, result);
            }
            else
            {
                var action = ParseActionLine(lines, ref pos, myIndent, line);
                if (action != null) result.Add(action);
            }
        }
        return result;
    }

    private static JsonObject ParseChoices(List<Line> lines, ref int pos, int myIndent)
    {
        var options = new JsonArray();
        while (pos < lines.Count && lines[pos].Indent >= myIndent)
        {
            var line = lines[pos];
            if (line.Indent != myIndent) { pos++; continue; }
            string t = line.Text;
            pos++;

            // "LABEL" -> NodeName  [if_flag:name | if_not_flag:name]
            if (!t.StartsWith('"')) continue;
            int closeQuote = t.IndexOf('"', 1);
            if (closeQuote < 0) continue;
            string label = t[1..closeQuote];
            string rest  = t[(closeQuote + 1)..].Trim();
            if (!rest.StartsWith("->")) continue;
            rest = rest[2..].Trim();
            string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string node = parts[0];

            var opt = new JsonObject { ["label"] = label, ["node"] = node };
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.StartsWith("if_flag:"))         opt["if_flag"]     = p[8..];
                else if (p.StartsWith("if_not_flag:")) opt["if_not_flag"] = p[12..];
            }
            options.Add(opt);
        }
        return new JsonObject { ["action"] = "choices", ["options"] = options };
    }

    // ── Handler map (thing/area body) ─────────────────────────────────────────

    private static JsonObject ParseHandlerMap(List<Line> lines, ref int pos, int myIndent)
    {
        var result = new JsonObject();
        while (pos < lines.Count && lines[pos].Indent >= myIndent)
        {
            var line = lines[pos];
            if (line.Indent != myIndent) { pos++; continue; }
            string t = line.Text;
            if (!t.StartsWith("on ") || !t.EndsWith(":")) { pos++; continue; }

            string verbPart   = t[3..^1].Trim();
            pos++;
            int actionIndent  = PeekIndent(lines, pos);
            var actions       = ParseActionList(lines, ref pos, actionIndent);

            if (verbPart.StartsWith("use_item "))
            {
                EnsureObj(result, "use_item")[verbPart[9..].Trim()] = actions;
            }
            else
            {
                // "walk, use" → "walk|use"
                result[verbPart.Replace(", ", "|").Replace(",", "|")] = actions;
            }
        }
        return result;
    }

    // ── Action list ───────────────────────────────────────────────────────────

    private static JsonArray ParseActionList(List<Line> lines, ref int pos, int myIndent)
    {
        var result = new JsonArray();
        while (pos < lines.Count && lines[pos].Indent >= myIndent)
        {
            var line = lines[pos];
            if (line.Indent != myIndent) { pos++; continue; }
            string t = line.Text;

            if (t.StartsWith("if ") && t.EndsWith(":"))
            {
                pos++;
                ParseIfBlock(lines, ref pos, myIndent, t, result);
            }
            else
            {
                var action = ParseActionLine(lines, ref pos, myIndent, line);
                if (action != null) result.Add(action);
            }
        }
        return result;
    }

    // ── if/else block ─────────────────────────────────────────────────────────

    private static void ParseIfBlock(List<Line> lines, ref int pos, int blockIndent, string ifLine, JsonArray result)
    {
        // "if [scope.]flagName is true/false:"  OR  "if [not] flagName:"
        string inner = ifLine[3..^1].Trim();
        string flagRef;
        bool   expected;
        int isIdx = inner.IndexOf(" is ", StringComparison.Ordinal);
        if (isIdx >= 0)
        {
            flagRef  = inner[..isIdx].Trim();
            expected = inner[(isIdx + 4)..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        else if (inner.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            flagRef  = inner[4..].Trim();
            expected = false;
        }
        else
        {
            flagRef  = inner;
            expected = true;
        }
        (string flagName, string scope) = ParseFlagRef(flagRef);

        int thenIndent  = PeekIndent(lines, pos);
        var thenActions = ParseActionList(lines, ref pos, thenIndent);

        JsonArray elseActions = null;
        if (pos < lines.Count && lines[pos].Indent == blockIndent && lines[pos].Text == "else:")
        {
            pos++;
            int elseIndent = PeekIndent(lines, pos);
            elseActions = ParseActionList(lines, ref pos, elseIndent);
        }

        var obj = new JsonObject
        {
            ["action"] = "if_flag",
            ["name"]   = flagName,
            ["is"]     = expected,
            ["then"]   = thenActions,
        };
        if (scope == "global") obj["scope"] = "global";
        if (elseActions != null) obj["else"] = elseActions;
        result.Add(obj);
    }

    // ── Single action line ────────────────────────────────────────────────────

    private static JsonObject ParseActionLine(List<Line> lines, ref int pos, int blockIndent, Line line)
    {
        pos++;
        string t = line.Text;

        // exit to destination [no_walk]   (sub-params on following indented lines)
        if (t.StartsWith("exit to "))
            return ParseExitAction(lines, ref pos, blockIndent, t);

        // wait <seconds>
        if (t.StartsWith("wait "))
        {
            float secs = float.TryParse(t[5..].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 1f;
            return new JsonObject { ["action"] = "wait", ["seconds"] = secs };
        }

        // narrate "text" [args]
        if (t.StartsWith("narrate "))
        {
            (string text, string args) = ExtractQuoted(t[8..].Trim());
            var obj = new JsonObject { ["action"] = "narrate", ["text"] = text };
            ApplyArgs(obj, args);
            return obj;
        }

        // obtain itemName
        if (t.StartsWith("obtain "))
            return new JsonObject { ["action"] = "add_to_inventory", ["item"] = t[7..].Trim() };

        // run sequenceName
        if (t.StartsWith("run "))
            return new JsonObject { ["action"] = "run_sequence", ["name"] = t[4..].Trim() };

        // finish_and_run sequenceName — ends the dialog and runs sequence via EventSequence
        if (t.StartsWith("finish_and_run "))
            return new JsonObject { ["action"] = "finish_and_run", ["name"] = t[15..].Trim() };

        // camera_pan <target|[x,y]> [duration:<float>] [strict]
        if (t.StartsWith("camera_pan "))
        {
            string rest    = t[11..].Trim();
            int    spcIdx  = rest.IndexOf(' ');
            string target  = spcIdx >= 0 ? rest[..spcIdx] : rest;
            string argsStr = spcIdx >= 0 ? rest[(spcIdx + 1)..] : "";
            var obj = new JsonObject { ["action"] = "camera_pan" };
            if (target.StartsWith("[") && target.EndsWith("]"))
            {
                var inner = target[1..^1].Split(',');
                obj["to"] = new JsonArray {
                    float.Parse(inner[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(inner[1].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                };
            }
            else
                obj["target"] = target;
            ApplyArgs(obj, argsStr);
            return obj;
        }

        // camera_zoom <factor> [target:<name>|[x,y]] [duration:<float>] [strict]
        if (t.StartsWith("camera_zoom "))
        {
            string rest    = t[12..].Trim();
            int    spcIdx  = rest.IndexOf(' ');
            string zoomStr = spcIdx >= 0 ? rest[..spcIdx] : rest;
            string argsStr = spcIdx >= 0 ? rest[(spcIdx + 1)..] : "";
            var obj = new JsonObject { ["action"] = "camera_zoom" };
            if (float.TryParse(zoomStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float zoom))
                obj["zoom"] = zoom;
            ApplyArgs(obj, argsStr);
            return obj;
        }

        // return_camera [duration:<float>] [strict]
        if (t == "return_camera" || t.StartsWith("return_camera "))
        {
            string argsStr = t.Length > 14 ? t[14..].Trim() : "";
            var obj = new JsonObject { ["action"] = "return_camera" };
            ApplyArgs(obj, argsStr);
            return obj;
        }

        // converse dialogName  →  resolves to res://game/data/dialogs/<name>.covscript
        if (t.StartsWith("converse "))
        {
            string name = t[9..].Trim();
            return new JsonObject { ["action"] = "conversation",
                                    ["file"]   = $"res://game/data/dialogs/{name}.covscript" };
        }

        // remap_floor polygonName [nav:regionName]
        if (t.StartsWith("remap_floor "))
        {
            string rest = t[12..].Trim();
            int    spc  = rest.IndexOf(' ');
            string polygon  = spc >= 0 ? rest[..spc] : rest;
            string navToken = spc >= 0 ? rest[(spc + 1)..].Trim() : "";
            var obj = new JsonObject { ["action"] = "remap_floor", ["polygon"] = polygon };
            if (navToken.StartsWith("nav:")) obj["nav_region"] = navToken[4..];
            return obj;
        }

        // show/hide/remove/restore target
        if (t.StartsWith("show "))
            return new JsonObject { ["action"] = "toggle_hide",  ["target"] = t[5..].Trim(), ["value"] = false };
        if (t.StartsWith("hide "))
            return new JsonObject { ["action"] = "toggle_hide",  ["target"] = t[5..].Trim(), ["value"] = true  };
        if (t.StartsWith("remove "))
            return new JsonObject { ["action"] = "toggle_exist", ["target"] = t[7..].Trim(), ["value"] = false };
        if (t.StartsWith("restore "))
            return new JsonObject { ["action"] = "toggle_exist", ["target"] = t[8..].Trim(), ["value"] = true  };

        // set [scope.]flag = value
        if (t.StartsWith("set "))
            return ParseSetFlag(t);

        // actor ~> target  (fly — straight line, ignores navigation region)
        if (t.Contains(" ~> "))
            return ParseFlyAction(t);

        // actor -> target [strict]
        if (t.Contains(" -> "))
            return ParseMoveAction(t);

        // actor says/thinks "text" [args]
        if (t.Contains(" says ") || t.Contains(" thinks "))
            return ParseSpeakAction(t);

        // actor faces <direction|thing>
        if (t.Contains(" faces "))
            return ParseFacingAction(t);

        // actor looks_at target
        if (t.Contains(" looks_at "))
        {
            int idx = t.IndexOf(" looks_at ", StringComparison.Ordinal);
            return new JsonObject { ["action"] = "look_at",
                                    ["actor"]  = t[..idx].Trim(),
                                    ["target"] = t[(idx + 10)..].Trim() };
        }

        // actor animates animationName
        if (t.Contains(" animates "))
        {
            int idx = t.IndexOf(" animates ", StringComparison.Ordinal);
            return new JsonObject { ["action"]    = "play_animation",
                                    ["actor"]     = t[..idx].Trim(),
                                    ["animation"] = t[(idx + 10)..].Trim() };
        }

        GD.PushWarning($"[CovScript] Unrecognized action on line {line.Number}: '{t}'");
        return null;
    }

    // ── Action-specific parsers ───────────────────────────────────────────────

    private static JsonObject ParseFlyAction(string t)
    {
        int arrowIdx = t.IndexOf(" ~> ", StringComparison.Ordinal);
        string actor = t[..arrowIdx].Trim();
        string rest  = t[(arrowIdx + 4)..].Trim();

        int spaceIdx   = rest.IndexOf(' ');
        string target  = spaceIdx >= 0 ? rest[..spaceIdx] : rest;
        string argsStr = spaceIdx >= 0 ? rest[(spaceIdx + 1)..] : "";

        var obj = new JsonObject { ["action"] = "fly", ["actor"] = actor };
        if (target.StartsWith("[") && target.EndsWith("]"))
        {
            var inner = target[1..^1].Split(',');
            obj["to"] = new JsonArray
            {
                float.Parse(inner[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(inner[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        else
        {
            obj["target"] = target;
        }
        ApplyArgs(obj, argsStr);
        return obj;
    }

    private static JsonObject ParseMoveAction(string t)
    {
        int arrowIdx = t.IndexOf(" -> ", StringComparison.Ordinal);
        string actor = t[..arrowIdx].Trim();
        string rest  = t[(arrowIdx + 4)..].Trim();

        // Split off trailing args (strict etc.) — target is the first token
        int spaceIdx = rest.IndexOf(' ');
        string target  = spaceIdx >= 0 ? rest[..spaceIdx] : rest;
        string argsStr = spaceIdx >= 0 ? rest[(spaceIdx + 1)..] : "";

        var obj = new JsonObject { ["action"] = "move", ["actor"] = actor };
        if (target.StartsWith("[") && target.EndsWith("]"))
        {
            var inner = target[1..^1].Split(',');
            obj["to"] = new JsonArray
            {
                float.Parse(inner[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(inner[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        else
        {
            obj["target"] = target;
        }
        ApplyArgs(obj, argsStr);
        return obj;
    }

    private static JsonObject ParseSpeakAction(string t)
    {
        bool   isThink = t.Contains(" thinks ");
        string verb    = isThink ? " thinks " : " says ";
        int    idx     = t.IndexOf(verb, StringComparison.Ordinal);
        string actor   = t[..idx].Trim();
        string rest    = t[(idx + verb.Length)..].Trim();
        (string text, string argsStr) = ExtractQuoted(rest);

        var obj = new JsonObject
        {
            ["action"] = isThink ? "think" : "speak",
            ["actor"]  = actor,
            ["text"]   = text,
        };
        ApplyArgs(obj, argsStr);
        return obj;
    }

    private static JsonObject ParseFacingAction(string t)
    {
        int    idx    = t.IndexOf(" faces ", StringComparison.Ordinal);
        string actor  = t[..idx].Trim();
        string target = t[(idx + 7)..].Trim();

        if (Directions.Contains(target))
            return new JsonObject { ["action"] = "change_facing", ["actor"] = actor, ["direction"] = target.ToLower() };
        else
            return new JsonObject { ["action"] = "look_at", ["actor"] = actor, ["target"] = target };
    }

    private static JsonObject ParseSetFlag(string t)
    {
        string rest  = t[4..].Trim();
        int    eqIdx = rest.IndexOf('=');
        if (eqIdx < 0) return null;
        (string flagName, string scope) = ParseFlagRef(rest[..eqIdx].Trim());
        string value = rest[(eqIdx + 1)..].Trim();

        var obj = new JsonObject { ["action"] = "set_flag", ["name"] = flagName, ["value"] = value };
        if (scope == "global") obj["scope"] = "global";
        return obj;
    }

    private static JsonObject ParseExitAction(List<Line> lines, ref int pos, int blockIndent, string t)
    {
        // "exit to destination [no_walk]"
        string rest   = t[8..].Trim();
        int    spcIdx = rest.IndexOf(' ');
        string dest   = spcIdx >= 0 ? rest[..spcIdx] : rest;
        bool   noWalk = rest.Contains("no_walk");

        var obj = new JsonObject { ["action"] = "exit", ["destination"] = dest };
        if (noWalk) obj["no_walk"] = true;

        // Consume indented sub-params: arrive_dir, arrive_area, arrive_thing, no_walk
        while (pos < lines.Count && lines[pos].Indent > blockIndent)
        {
            string sub = lines[pos].Text;
            pos++;
            if      (sub.StartsWith("arrive_dir "))   obj["arrive_dir"]   = sub[11..].Trim();
            else if (sub.StartsWith("arrive_area "))  obj["arrive_area"]  = sub[12..].Trim();
            else if (sub.StartsWith("arrive_thing ")) obj["arrive_thing"] = sub[13..].Trim();
            else if (sub == "no_walk")                obj["no_walk"]      = true;
        }
        return obj;
    }

    // ── Arg parser ────────────────────────────────────────────────────────────

    // Applies trailing space-separated args to an action object.
    // Recognised tokens: strict, for N, style:x, color:x, corner:x
    private static void ApplyArgs(JsonObject obj, string argsStr)
    {
        if (string.IsNullOrWhiteSpace(argsStr)) return;
        string[] tokens = argsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if      (token == "strict")             obj["strict"] = true;
            else if (token == "for" && i + 1 < tokens.Length && float.TryParse(tokens[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float secs))
                                                  { obj["for"] = secs; i++; }
            else if (token.StartsWith("style:"))    obj["style"]    = token[6..];
            else if (token.StartsWith("color:"))    obj["color"]    = token[6..];
            else if (token.StartsWith("corner:"))   obj["corner"]   = token[7..];
            else if (token.StartsWith("duration:") && float.TryParse(token[9..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dur))
                                                    obj["duration"] = dur;
            else if (token.StartsWith("target:"))   obj["target"]   = token[7..];
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Extracts the leading "quoted string" from s, returns (text, remaining_args).
    private static (string text, string rest) ExtractQuoted(string s)
    {
        if (!s.StartsWith('"')) return (s, "");
        int end = s.IndexOf('"', 1);
        if (end < 0) return (s[1..], "");
        return (s[1..end].Replace("\\n", "\n"), s[(end + 1)..].Trim());
    }

    // Parses "scene.flag", "global.flag", or bare "flag" → (name, scope).
    private static (string name, string scope) ParseFlagRef(string s)
    {
        int dot = s.IndexOf('.');
        if (dot < 0) return (s, "scene");
        string prefix = s[..dot].ToLower();
        return (s[(dot + 1)..], prefix == "global" ? "global" : "scene");
    }

    private static int PeekIndent(List<Line> lines, int pos)
        => pos < lines.Count ? lines[pos].Indent : 0;

    private static JsonObject EnsureObj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing) return existing;
        var obj = new JsonObject();
        parent[key] = obj;
        return obj;
    }
}
