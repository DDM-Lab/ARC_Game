using System;
using System.Collections.Generic;

namespace ARC.Replay
{
    public sealed class StateDeltaException : Exception
    {
        public StateDeltaException(string message) : base(message) { }
    }

    // Window deltas over canonical state trees. A delta holds the change between the previous
    // captured state and this one, as an ordered list of ops. Paths are arrays of keys rather
    // than dotted strings, so an id that contains a dot cannot be read as nesting.
    //
    // OPS (every value is a canonical JSON node):
    //   set     {op, path, from, value}  leaf -> leaf. "from" must equal the current value.
    //   add     {op, path, value}        key absent -> present. "value" is the whole subtree.
    //   remove  {op, path, fromHash}     key present -> absent. "fromHash" is the SHA-256 of the
    //                                    removed subtree's canonical text, so removing the wrong
    //                                    thing fails loudly instead of silently.
    // A key that changes between object and non-object becomes remove followed by add.
    // Only keys are removed. The root is never removed or replaced, so every path is non-empty.
    //
    // Leaves are every non-object value, arrays included. Keyed lists are already objects (see
    // CanonicalState), so arrays here are the ordered primitive lists, which are replaced whole.
    public static class StateDiff
    {
        // ---- diff -----------------------------------------------------------------------

        public static List<JObject> Diff(JObject before, JObject after)
        {
            var ops = new List<JObject>();
            DiffObjects(before, after, new List<string>(), ops);
            return ops;
        }

        static void DiffObjects(JObject before, JObject after, List<string> parent, List<JObject> ops)
        {
            foreach (var kv in before.Members)
            {
                if (!after.Members.ContainsKey(kv.Key))
                    ops.Add(RemoveOp(Extend(parent, kv.Key), kv.Value));
            }

            foreach (var kv in after.Members)
            {
                List<string> path = Extend(parent, kv.Key);
                JNode old;
                if (!before.Members.TryGetValue(kv.Key, out old))
                {
                    ops.Add(AddOp(path, kv.Value));
                    continue;
                }

                var oldObj = old as JObject;
                var newObj = kv.Value as JObject;
                if (oldObj != null && newObj != null)
                {
                    DiffObjects(oldObj, newObj, path, ops);
                }
                else if (oldObj != null || newObj != null)
                {
                    ops.Add(RemoveOp(path, old));
                    ops.Add(AddOp(path, kv.Value));
                }
                else if (Canon(old) != Canon(kv.Value))
                {
                    ops.Add(SetOp(path, old, kv.Value));
                }
            }
        }

        // ---- delta document ---------------------------------------------------------------

        public static JObject BuildDelta(int baseSeq, string baseHash, string resultHash, List<JObject> ops)
        {
            var opsArray = new JArray();
            foreach (JObject op in ops) opsArray.Items.Add(op);

            var delta = new JObject();
            delta.Members["baseSeq"] = new JNumber(baseSeq);
            delta.Members["baseHash"] = new JString(baseHash);
            delta.Members["resultHash"] = new JString(resultHash);
            delta.Members["ops"] = opsArray;
            return delta;
        }

        // ---- apply ----------------------------------------------------------------------

        // Checks the base hash, applies every op in order, then checks the result hash. The state
        // is modified in place, so apply to a Clone when the original must survive a failure.
        public static void ApplyDelta(JObject state, JObject delta)
        {
            string baseHash = StringField(delta, "baseHash");
            string resultHash = StringField(delta, "resultHash");
            if (Hash(state) != baseHash)
                throw new StateDeltaException("base hash mismatch before applying delta");

            JArray ops = ArrayField(delta, "ops");
            foreach (JNode node in ops.Items)
            {
                var op = node as JObject;
                if (op == null) throw new StateDeltaException("op is not an object");
                ApplyOp(state, op);
            }

            if (Hash(state) != resultHash)
                throw new StateDeltaException("result hash mismatch after applying delta");
        }

        static void ApplyOp(JObject state, JObject op)
        {
            string kind = StringField(op, "op");
            JArray path = ArrayField(op, "path");
            string key;
            JObject parent = ParentOf(state, path, out key);

            switch (kind)
            {
                case "set":
                {
                    JNode current = Current(parent, key);
                    if (current is JObject) throw new StateDeltaException("set targets an object at " + PathText(path));
                    if (Canon(current) != Canon(Field(op, "from")))
                        throw new StateDeltaException("set 'from' does not match current value at " + PathText(path));
                    parent.Members[key] = Clone(Field(op, "value"));
                    break;
                }
                case "add":
                {
                    if (parent.Members.ContainsKey(key))
                        throw new StateDeltaException("add targets an existing key at " + PathText(path));
                    parent.Members[key] = Clone(Field(op, "value"));
                    break;
                }
                case "remove":
                {
                    JNode current = Current(parent, key);
                    if (Hash(current) != StringField(op, "fromHash"))
                        throw new StateDeltaException("remove 'fromHash' does not match current value at " + PathText(path));
                    parent.Members.Remove(key);
                    break;
                }
                default:
                    throw new StateDeltaException("unknown op '" + kind + "'");
            }
        }

        // Walks to the object that holds the last path key. Every intermediate key must exist
        // and be an object.
        static JObject ParentOf(JObject state, JArray path, out string lastKey)
        {
            if (path.Items.Count == 0) throw new StateDeltaException("op path is empty");
            JObject cur = state;
            for (int i = 0; i < path.Items.Count - 1; i++)
            {
                string k = StringOf(path.Items[i], "path");
                JNode next = Current(cur, k);
                cur = next as JObject;
                if (cur == null) throw new StateDeltaException("path passes through a non-object at " + PathText(path));
            }
            lastKey = StringOf(path.Items[path.Items.Count - 1], "path");
            return cur;
        }

        static JNode Current(JObject parent, string key)
        {
            JNode node;
            if (!parent.Members.TryGetValue(key, out node))
                throw new StateDeltaException("missing key '" + key + "'");
            return node;
        }

        // ---- helpers --------------------------------------------------------------------

        static JObject RemoveOp(List<string> path, JNode old)
        {
            JObject op = Op("remove", path);
            op.Members["fromHash"] = new JString(Hash(old));
            return op;
        }

        static JObject AddOp(List<string> path, JNode value)
        {
            JObject op = Op("add", path);
            op.Members["value"] = Clone(value);
            return op;
        }

        static JObject SetOp(List<string> path, JNode from, JNode value)
        {
            JObject op = Op("set", path);
            op.Members["from"] = Clone(from);
            op.Members["value"] = Clone(value);
            return op;
        }

        static JObject Op(string kind, List<string> path)
        {
            var op = new JObject();
            op.Members["op"] = new JString(kind);
            var pathArray = new JArray();
            foreach (string p in path) pathArray.Items.Add(new JString(p));
            op.Members["path"] = pathArray;
            return op;
        }

        static List<string> Extend(List<string> parent, string key)
        {
            var path = new List<string>(parent.Count + 1);
            path.AddRange(parent);
            path.Add(key);
            return path;
        }

        public static JNode Clone(JNode node)
        {
            var obj = node as JObject;
            if (obj != null)
            {
                var copy = new JObject();
                foreach (var kv in obj.Members) copy.Members.Add(kv.Key, Clone(kv.Value));
                return copy;
            }
            var arr = node as JArray;
            if (arr != null)
            {
                var copy = new JArray();
                foreach (JNode item in arr.Items) copy.Items.Add(Clone(item));
                return copy;
            }
            return node; // leaves are immutable
        }

        static string Canon(JNode node) => JsonCanon.ToCanonicalString(node);

        static string Hash(JNode node) => JsonCanon.Sha256Hex(Canon(node));

        static JNode Field(JObject obj, string name)
        {
            JNode node;
            if (!obj.Members.TryGetValue(name, out node))
                throw new StateDeltaException("missing field '" + name + "'");
            return node;
        }

        static string StringField(JObject obj, string name)
        {
            return StringOf(Field(obj, name), name);
        }

        static JArray ArrayField(JObject obj, string name)
        {
            var arr = Field(obj, name) as JArray;
            if (arr == null) throw new StateDeltaException("field '" + name + "' is not an array");
            return arr;
        }

        static string StringOf(JNode node, string what)
        {
            var s = node as JString;
            if (s == null) throw new StateDeltaException(what + " entry is not a string");
            return s.Value;
        }

        static string PathText(JArray path)
        {
            var sb = new System.Text.StringBuilder();
            foreach (JNode n in path.Items)
            {
                if (sb.Length > 0) sb.Append('/');
                var s = n as JString;
                sb.Append(s != null ? s.Value : "?");
            }
            return sb.ToString();
        }
    }
}
