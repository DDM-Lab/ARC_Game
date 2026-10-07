using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARC.Replay;
using NUnit.Framework;
using UnityEngine;

namespace ARC.Replay.Tests
{
    // Runs the shared golden cases (tests/replay/canonical_cases.tsv, generated from
    // canonical_vectors.json by make_canonical_cases.py) through the real canonicalisation code.
    // The same cases pass in Python (tests/replay/test_canonical.py) and in a plain Mono build
    // of these sources, so a failure here means the Unity side has diverged from the others.
    public class CanonicalReplayTests
    {
        static string CasesPath
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tests", "replay", "canonical_cases.tsv")); }
        }

        static string Decode(string base64)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        [Test]
        public void SharedGoldenCasesMatch()
        {
            var failures = new List<string>();
            int count = 0;

            foreach (string line in File.ReadAllLines(CasesPath, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                count++;

                string[] f = line.Split('\t');
                string kind = f[0];
                string input = Decode(f[1]);
                bool expectError = f[2] == "ERROR";
                string expected = expectError ? null : Decode(f[2]);
                string expectedHash = f.Length > 3 ? f[3] : "";

                string actual = null;
                string actualHash = null;
                string error = null;
                try
                {
                    if (kind == "json")
                    {
                        actual = JsonCanon.Canonicalize(input);
                    }
                    else
                    {
                        CanonicalResult result = CanonicalState.FromSnapshotJson(input);
                        actual = result.Json;
                        actualHash = result.Hash;
                    }
                }
                catch (Exception e)
                {
                    error = e.GetType().Name + ": " + e.Message;
                }

                if (expectError)
                {
                    if (error == null) failures.Add("expected error, got " + actual + " for: " + input);
                    continue;
                }
                if (error != null)
                {
                    failures.Add("unexpected " + error + " for: " + input);
                    continue;
                }
                if (actual != expected)
                    failures.Add("mismatch for: " + input + "\n  expected " + expected + "\n  actual   " + actual);
                if (expectedHash.Length > 0 && actualHash != expectedHash)
                    failures.Add("hash mismatch for: " + input);
            }

            Assert.That(count, Is.GreaterThan(0), "no cases read from " + CasesPath);
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void SixDecimalRoundingTiesGoToEven()
        {
            Assert.That(JsonCanon.FormatNumber(0.0000025m), Is.EqualTo("0.000002"));
            Assert.That(JsonCanon.FormatNumber(0.0000035m), Is.EqualTo("0.000004"));
            Assert.That(JsonCanon.FormatNumber(-0.0000004m), Is.EqualTo("0"));
        }

        [Test]
        public void VolatileRootFieldsAreExcludedFromHash()
        {
            string a = "{\"createdUtc\":\"2026-01-01T00:00:00Z\",\"seed\":5}";
            string b = "{\"createdUtc\":\"2030-06-06T06:06:06Z\",\"seed\":99}";
            Assert.That(CanonicalState.FromSnapshotJson(a).Hash, Is.EqualTo(CanonicalState.FromSnapshotJson(b).Hash));
        }
    }
}
