using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARC.Replay;
using NUnit.Framework;
using UnityEngine;

namespace ARC.Replay.Tests
{
    // Delta tests for StateDiff. tests/replay/delta_cases.tsv holds (before, after, delta) triples
    // written by the C# harness (tests/replay/csharp_harness). Here each delta must apply to its
    // before state and give exactly the after state. The corruption cases must be rejected.
    public class StateDiffTests
    {
        static string DeltaCasesPath
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tests", "replay", "delta_cases.tsv")); }
        }

        static string Decode(string base64)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        [Test]
        public void SharedDeltaCasesApplyToTheirAfterState()
        {
            var failures = new List<string>();
            int count = 0;
            foreach (string line in File.ReadAllLines(DeltaCasesPath, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                count++;
                string[] f = line.Split('\t');
                string beforeText = Decode(f[0]);
                string afterText = Decode(f[1]);
                string deltaText = Decode(f[2]);

                try
                {
                    JObject before = CanonicalState.FromSnapshotJson(beforeText).Tree;
                    JObject delta = (JObject)JsonCanon.Parse(deltaText);
                    StateDiff.ApplyDelta(before, delta);
                    string applied = JsonCanon.ToCanonicalString(before);
                    if (applied != afterText) failures.Add("case " + count + ": applied state differs");
                }
                catch (Exception e)
                {
                    failures.Add("case " + count + ": " + e.GetType().Name + ": " + e.Message);
                }
            }

            Assert.That(count, Is.GreaterThan(0), "no delta cases read from " + DeltaCasesPath);
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void DeltaWithWrongBaseHashIsRejected()
        {
            string beforeText = Decode(File.ReadAllLines(DeltaCasesPath, Encoding.UTF8)[0].Split('\t')[0]);
            JObject before = CanonicalState.FromSnapshotJson(beforeText).Tree;
            JObject delta = StateDiff.BuildDelta(0, new string('0', 64), new string('1', 64), new List<JObject>());

            Assert.Throws<StateDeltaException>(() => StateDiff.ApplyDelta(before, delta));
        }
    }
}
