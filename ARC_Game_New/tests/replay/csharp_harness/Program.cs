using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARC.Replay;

// Cross-language harness. Runs the REAL Assets/Scripts/GameLog/Replay sources (compiled, not
// copied) against the shared cases and the delta checks.
//
// Build and run with the Mono that ships inside Unity (no SDK needed). From the repo root:
//
//   M="<Unity>/Editor/Data/MonoBleedingEdge"
//   R=Assets/Scripts/GameLog/Replay
//   "$M/bin/mono.exe" "$M/lib/mono/msbuild/Current/bin/Roslyn/csc.exe" -nologo -langversion:latest \
//       -target:exe -out:tests/replay/csharp_harness/harness.exe \
//       tests/replay/csharp_harness/*.cs $R/CanonicalJson.cs $R/CanonicalState.cs $R/StateDiff.cs
//   "$M/bin/mono.exe" tests/replay/csharp_harness/harness.exe tests/replay/canonical_cases.tsv \
//       --fixture tests/replay/fixtures/snapshot_synthetic.json --write-deltas tests/replay/delta_cases.tsv
//
// Each case line is   kind \t base64(input) \t base64(expected) or ERROR \t expectedHash (optional).
// Base64 keeps the harness free of any JSON parser, so it cannot share a bug with the code under test.
static class Program
{
    static int pass;
    static int fail;

    static string Dec(string b64)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }

    static void Report(bool ok, string name, string detail)
    {
        if (ok) { pass++; return; }
        fail++;
        Console.WriteLine("FAIL " + name + "\n     " + detail);
    }

    static string Short(string s)
    {
        if (s == null) return "null";
        return s.Length > 160 ? s.Substring(0, 160) + "..." : s;
    }

    static void RunCases(string path)
    {
        int count = 0;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0) continue;
            count++;
            string[] f = line.Split('\t');
            string kind = f[0];
            string input = Dec(f[1]);
            bool expectError = f[2] == "ERROR";
            string expected = expectError ? null : Dec(f[2]);
            string expectedHash = f.Length > 3 ? f[3] : "";
            string name = kind + "#" + count + " " + Short(input);

            string actual = null;
            string actualHash = null;
            string error = null;
            try
            {
                if (kind == "json")
                {
                    actual = JsonCanon.Canonicalize(input);
                }
                else if (kind == "snapshot")
                {
                    CanonicalResult r = CanonicalState.FromSnapshotJson(input);
                    actual = r.Json;
                    actualHash = r.Hash;
                }
                else
                {
                    error = "unknown kind " + kind;
                }
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }

            if (expectError)
            {
                Report(error != null, name, "expected an error but got: " + Short(actual));
                continue;
            }
            if (error != null)
            {
                Report(false, name, "unexpected exception " + error);
                continue;
            }
            Report(actual == expected, name,
                "canonical mismatch\n     expected " + Short(expected) + "\n     actual   " + Short(actual));
            if (expectedHash.Length > 0)
                Report(actualHash == expectedHash, name + " [hash]", "expected " + expectedHash + " got " + actualHash);
        }
        Console.WriteLine(path + ": " + count + " cases");
    }

    static int Main(string[] args)
    {
        var caseFiles = new List<string>();
        string fixture = null;
        string writeDeltas = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--fixture") fixture = args[++i];
            else if (args[i] == "--write-deltas") writeDeltas = args[++i];
            else caseFiles.Add(args[i]);
        }

        foreach (string file in caseFiles) RunCases(file);
        if (fixture != null) fail += DeltaChecks.Run(fixture, writeDeltas);

        Console.WriteLine("TOTAL pass: " + pass + "  fail: " + fail);
        return fail == 0 ? 0 : 1;
    }
}
