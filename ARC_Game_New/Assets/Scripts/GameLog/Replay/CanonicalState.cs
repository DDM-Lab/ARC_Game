using System;
using System.Collections.Generic;

namespace ARC.Replay
{
    public sealed class CanonicalStateException : Exception
    {
        public CanonicalStateException(string message) : base(message) { }
    }

    public sealed class CanonicalResult
    {
        public string Json;   // canonical text, the exact bytes that were hashed
        public string Hash;   // SHA-256 hex of Json
        public JObject Tree;  // the same state as a tree, so the next delta can be diffed against it
    }

    // Turns a GameSnapshot JSON (JsonUtility output) into the canonical state that keyframes
    // and deltas are defined over.
    //
    // WHAT CHANGES AND WHAT DOES NOT
    //  - Volatile root fields are removed (createdUtc is wall-clock; seed is an episode
    //    constant recorded once in the export header).
    //  - Lists of entities with a stable id become objects keyed by that id (KeyRules), so a
    //    delta says "walk 7 changed" rather than "element 3 changed", and removing an earlier
    //    item cannot shift every later index.
    //  - Every other value is kept as it is. Numbers are canonicalised (6 dp), object keys are
    //    sorted, and ordered arrays such as cargoTypes/cargoAmounts remain arrays. Those are
    //    correct but coarser: a change to one element is a whole-array replacement.
    //  - The root carries schemaVersion so a hash can never match across a schema change.
    public static class CanonicalState
    {
        public const int SchemaVersion = 1;

        static readonly string[] ExcludedRootKeys = { "createdUtc", "seed" };

        // Dotted path from the snapshot root -> id field. Every array at that path must hold
        // objects that carry the field, with unique values, or the state cannot be keyed and
        // the transform throws (so a gap is visible rather than silently mis-keyed).
        static readonly KeyRule[] KeyRules =
        {
            new KeyRule("buildings", "originalSiteId"),
            new KeyRule("prebuilt", "buildingName"),
            new KeyRule("vehicles", "vehicleName"),
            new KeyRule("workforce.workers", "workerId"),
            new KeyRule("tasks.activeTasks", "uid"),
            new KeyRule("tasks.completedTasks", "uid"),
            new KeyRule("relocations.walks", "walkId"),
            new KeyRule("budgetAllocations.pending", "allocId"),
            new KeyRule("deliveries.active", "taskId"),
            new KeyRule("deliveries.completed", "taskId"),
            new KeyRule("deliveries.pending", "taskId"),
            new KeyRule("clients.groups", "groupId"),
        };

        public static CanonicalResult FromSnapshotJson(string snapshotJson)
        {
            JNode parsed = JsonCanon.Parse(snapshotJson);
            var root = parsed as JObject;
            if (root == null) throw new CanonicalStateException("snapshot root is not a JSON object");

            foreach (string key in ExcludedRootKeys) root.Members.Remove(key);
            root.Members["schemaVersion"] = new JNumber(SchemaVersion);

            JObject state = (JObject)Transform(root, "");
            string json = JsonCanon.ToCanonicalString(state);
            return new CanonicalResult { Json = json, Hash = JsonCanon.Sha256Hex(json), Tree = state };
        }

        static JNode Transform(JNode node, string path)
        {
            if (node is JObject obj)
            {
                var result = new JObject();
                foreach (var kv in obj.Members)
                {
                    string childPath = path.Length == 0 ? kv.Key : path + "." + kv.Key;
                    result.Members.Add(kv.Key, Transform(kv.Value, childPath));
                }
                return result;
            }

            if (node is JArray arr)
            {
                KeyRule rule = FindRule(path);
                if (rule != null) return KeyArray(arr, path, rule.IdField);

                var result = new JArray();
                foreach (JNode item in arr.Items) result.Items.Add(Transform(item, path + "[]"));
                return result;
            }

            return node;
        }

        static JObject KeyArray(JArray arr, string path, string idField)
        {
            var keyed = new JObject();
            foreach (JNode item in arr.Items)
            {
                var element = item as JObject;
                if (element == null) throw new CanonicalStateException(path + " holds a non-object element");

                JNode idNode;
                if (!element.Members.TryGetValue(idField, out idNode))
                    throw new CanonicalStateException(path + " element is missing '" + idField + "'");

                string key = IdText(idNode, path, idField);
                if (keyed.Members.ContainsKey(key))
                    throw new CanonicalStateException(path + " has duplicate " + idField + " " + key);

                keyed.Members.Add(key, Transform(element, path + "[]"));
            }
            return keyed;
        }

        static string IdText(JNode idNode, string path, string idField)
        {
            if (idNode is JString s) return s.Value;
            if (idNode is JNumber n) return JsonCanon.FormatNumber(n.Value);
            throw new CanonicalStateException(path + " id '" + idField + "' is not a string or number");
        }

        static KeyRule FindRule(string path)
        {
            foreach (KeyRule rule in KeyRules)
            {
                if (rule.Path == path) return rule;
            }
            return null;
        }

        sealed class KeyRule
        {
            public readonly string Path;
            public readonly string IdField;
            public KeyRule(string path, string idField) { Path = path; IdField = idField; }
        }
    }
}
