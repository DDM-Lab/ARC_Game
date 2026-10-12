using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARC.Replay;

// Delta tests for StateDiff, run against the real Assets/Scripts/GameLog/Replay sources.
// Each mutation is diffed, the delta is applied to a copy of the base, and the result must be
// byte-identical to the mutated state. Corruptions must be rejected. Optionally writes the
// (before, after, delta) triples so Python can apply the C# deltas too (tests/replay/test_deltas.py).
static class DeltaChecks
{
    static int pass;
    static int fail;

    static void Check(bool ok, string name, string detail)
    {
        if (ok) { pass++; return; }
        fail++;
        Console.WriteLine("FAIL " + name + "\n     " + detail);
    }

    static JNode At(JNode root, params string[] path)
    {
        JNode cur = root;
        foreach (string k in path) cur = ((JObject)cur).Members[k];
        return cur;
    }

    static void Put(JObject root, string[] parentPath, string key, JNode value)
    {
        ((JObject)At(root, parentPath)).Members[key] = value;
    }

    static void Remove(JObject root, string[] parentPath, string key)
    {
        ((JObject)At(root, parentPath)).Members.Remove(key);
    }

    static JNode N(decimal d) { return new JNumber(d); }
    static JNode S(string s) { return new JString(s); }
    static JNode O() { return new JObject(); }

    static JArray A(params JNode[] items)
    {
        var a = new JArray();
        a.Items.AddRange(items);
        return a;
    }

    static string Canon(JNode n) { return JsonCanon.ToCanonicalString(n); }
    static string Hash(JNode n) { return JsonCanon.Sha256Hex(Canon(n)); }

    static JObject Copy(JObject o) { return (JObject)StateDiff.Clone(o); }

    static string B64(string text) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }

    static List<KeyValuePair<string, Action<JObject>>> Mutations()
    {
        var m = new List<KeyValuePair<string, Action<JObject>>>();
        m.Add(new KeyValuePair<string, Action<JObject>>("no change", r => { }));
        m.Add(new KeyValuePair<string, Action<JObject>>("set number", r => Put(r, new[] { "economy" }, "currentBudget", N(9000))));
        m.Add(new KeyValuePair<string, Action<JObject>>("remove keyed walk", r => Remove(r, new[] { "relocations", "walks" }, "3")));
        m.Add(new KeyValuePair<string, Action<JObject>>("add keyed vehicle", r =>
            Put(r, new[] { "vehicles" }, "Vehicle 3", StateDiff.Clone(At(r, "vehicles", "Vehicle 1")))));
        m.Add(new KeyValuePair<string, Action<JObject>>("nested number", r =>
            Put(r, new[] { "vehicles", "Vehicle 2", "position" }, "x", N(2.25m))));
        m.Add(new KeyValuePair<string, Action<JObject>>("leaf to object", r => Put(r, new[] { "economy" }, "currentEfficiency", O())));
        m.Add(new KeyValuePair<string, Action<JObject>>("array replaced", r =>
            Put(r, new[] { "vehicles", "Vehicle 2" }, "cargoAmounts", A(N(10), N(20)))));
        m.Add(new KeyValuePair<string, Action<JObject>>("remove keyed task", r => Remove(r, new[] { "tasks", "activeTasks" }, "9")));
        m.Add(new KeyValuePair<string, Action<JObject>>("object to leaf", r => Put(r, new string[0], "clock", N(5))));
        m.Add(new KeyValuePair<string, Action<JObject>>("deep add", r =>
        {
            var probe = new JObject();
            probe.Members["inner"] = A();
            Put(r, new[] { "dailyReport" }, "probe", probe);
        }));
        m.Add(new KeyValuePair<string, Action<JObject>>("delete top-level", r => Remove(r, new string[0], "rewardMetrics")));
        m.Add(new KeyValuePair<string, Action<JObject>>("string change", r => Put(r, new[] { "weather" }, "current", S("Storm"))));
        m.Add(new KeyValuePair<string, Action<JObject>>("key containing a dot", r => Put(r, new[] { "flood" }, "x.y", N(1))));
        m.Add(new KeyValuePair<string, Action<JObject>>("combined", r =>
        {
            Put(r, new[] { "economy" }, "currentBudget", N(9000));
            Put(r, new[] { "vehicles" }, "Vehicle 3", StateDiff.Clone(At(r, "vehicles", "Vehicle 1")));
            Remove(r, new[] { "tasks", "activeTasks" }, "9");
        }));
        return m;
    }

    // Returns the number of failed checks, so the caller can fold them into its own total.
    public static int Run(string fixturePath, string writeTsvPath)
    {
        JObject before = CanonicalState.FromSnapshotJson(File.ReadAllText(fixturePath, Encoding.UTF8)).Tree;
        string beforeCanon = Canon(before);
        var tsv = new StringBuilder();
        var mutations = Mutations();

        foreach (var mutation in mutations)
        {
            JObject after = Copy(before);
            mutation.Value(after);
            List<JObject> ops = StateDiff.Diff(before, after);
            JObject delta = StateDiff.BuildDelta(5, Hash(before), Hash(after), ops);
            string deltaText = Canon(delta);

            JObject work = Copy(before);
            try
            {
                StateDiff.ApplyDelta(work, delta);
                Check(Canon(work) == Canon(after), mutation.Key, "applied state differs from mutated state");
            }
            catch (Exception e)
            {
                Check(false, mutation.Key, e.GetType().Name + ": " + e.Message);
            }

            if (mutation.Key == "no change")
                Check(ops.Count == 0, mutation.Key, "expected zero ops, got " + ops.Count);
            else
                Check(ops.Count > 0 || Canon(before) == Canon(after), mutation.Key, "changed state produced no ops");

            // Determinism: the same diff gives the same delta text.
            Check(Canon(StateDiff.BuildDelta(5, Hash(before), Hash(after), StateDiff.Diff(before, after))) == deltaText,
                mutation.Key + " [deterministic]", "two diffs of the same states differ");

            tsv.Append(B64(beforeCanon)).Append('\t').Append(B64(Canon(after))).Append('\t').Append(B64(deltaText)).Append('\n');
        }

        RunCorruptions(before, mutations);
        RunChain(before, mutations);

        if (writeTsvPath != null)
        {
            File.WriteAllText(writeTsvPath, tsv.ToString(), new UTF8Encoding(false));
            Console.WriteLine("wrote " + writeTsvPath);
        }
        Console.WriteLine("delta checks: pass " + pass + " fail " + fail);
        return fail;
    }

    static Action<JObject> Find(List<KeyValuePair<string, Action<JObject>>> list, string name)
    {
        foreach (var kv in list) if (kv.Key == name) return kv.Value;
        throw new InvalidOperationException("no mutation " + name);
    }

    static void ExpectReject(string name, JObject baseState, JObject delta)
    {
        try
        {
            StateDiff.ApplyDelta(Copy(baseState), delta);
            Check(false, name, "corrupted delta was accepted");
        }
        catch (StateDeltaException)
        {
            Check(true, name, "");
        }
    }

    static void RunCorruptions(JObject before, List<KeyValuePair<string, Action<JObject>>> mutations)
    {
        JObject combined = Copy(before);
        Find(mutations, "combined")(combined);
        string beforeHash = Hash(before);
        string combinedHash = Hash(combined);

        // 1. A set op whose 'from' no longer matches the base.
        List<JObject> ops = StateDiff.Diff(before, combined);
        JObject wrongFrom = null;
        foreach (JObject op in ops)
        {
            if (((JString)op.Members["op"]).Value == "set")
            {
                wrongFrom = Copy(op);
                wrongFrom.Members["from"] = N(1);
                break;
            }
        }
        if (wrongFrom != null)
        {
            var corrupted = new List<JObject>();
            bool swapped = false;
            foreach (JObject op in ops)
            {
                if (!swapped && ((JString)op.Members["op"]).Value == "set") { corrupted.Add(wrongFrom); swapped = true; }
                else corrupted.Add(op);
            }
            ExpectReject("corrupt set 'from'", before, StateDiff.BuildDelta(5, beforeHash, combinedHash, corrupted));
        }

        // 2. A dropped op changes the result.
        var dropped = new List<JObject>(ops);
        dropped.RemoveAt(dropped.Count - 1);
        ExpectReject("dropped last op", before, StateDiff.BuildDelta(5, beforeHash, combinedHash, dropped));

        // 3. A wrong base hash.
        ExpectReject("wrong baseHash", before, StateDiff.BuildDelta(5, new string('0', 64), combinedHash, ops));

        // 4. A remove whose fromHash does not match the removed subtree.
        JObject removeCase = Copy(before);
        Find(mutations, "remove keyed walk")(removeCase);
        List<JObject> removeOps = StateDiff.Diff(before, removeCase);
        var badRemove = new List<JObject>();
        foreach (JObject op in removeOps)
        {
            if (((JString)op.Members["op"]).Value == "remove")
            {
                JObject copy = Copy(op);
                copy.Members["fromHash"] = S(new string('f', 64));
                badRemove.Add(copy);
            }
            else badRemove.Add(op);
        }
        ExpectReject("wrong fromHash on remove", before, StateDiff.BuildDelta(5, beforeHash, Hash(removeCase), badRemove));

        // 5. Type change reversed: add before remove on the same key.
        JObject typeCase = Copy(before);
        Find(mutations, "object to leaf")(typeCase);
        List<JObject> typeOps = StateDiff.Diff(before, typeCase);
        var reversed = new List<JObject>(typeOps);
        reversed.Reverse();
        ExpectReject("type change reversed", before, StateDiff.BuildDelta(5, beforeHash, Hash(typeCase), reversed));

        // 6. A duplicated op.
        var duplicated = new List<JObject>(ops);
        duplicated.Add(ops[0]);
        ExpectReject("duplicated op", before, StateDiff.BuildDelta(5, beforeHash, combinedHash, duplicated));

        // 7. An op whose path goes through a key that does not exist.
        var bogus = new JObject();
        bogus.Members["op"] = S("set");
        var bogusPath = new JArray();
        bogusPath.Items.Add(S("economy"));
        bogusPath.Items.Add(S("doesNotExist"));
        bogus.Members["path"] = bogusPath;
        bogus.Members["from"] = N(1);
        bogus.Members["value"] = N(2);
        ExpectReject("path to missing key", before, StateDiff.BuildDelta(5, beforeHash, beforeHash, new List<JObject> { bogus }));
    }

    static void RunChain(JObject before, List<KeyValuePair<string, Action<JObject>>> mutations)
    {
        // Successive window deltas. Each base hash must be the previous result hash.
        string[] steps = { "set number", "remove keyed walk", "add keyed vehicle", "nested number",
                           "leaf to object", "array replaced", "remove keyed task" };
        JObject current = Copy(before);
        foreach (string step in steps)
        {
            JObject next = Copy(current);
            Find(mutations, step)(next);
            JObject delta = StateDiff.BuildDelta(0, Hash(current), Hash(next), StateDiff.Diff(current, next));
            JObject replayed = Copy(current);
            try
            {
                StateDiff.ApplyDelta(replayed, delta);
                Check(Canon(replayed) == Canon(next), "chain " + step, "replayed state differs");
            }
            catch (Exception e)
            {
                Check(false, "chain " + step, e.Message);
            }
            current = next;
        }
    }
}
