#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Runs the log upload (LogSender, CoraUpload.jslib, EndOfGamePanel's status text) through a set of
/// network faults in a real WebGL build, against tests/upload_browser/fault_server.py, and posts the
/// results to its /report. Development builds only; the scene is made by
/// Editor/UploadSelfTestBuild.cs and the run is driven by tests/upload_browser/run_webgl_selftest.py.
/// </summary>
public class UploadSelfTest : MonoBehaviour
{
    [Serializable]
    class ScenarioResult
    {
        public string name;
        public bool pass;
        public string problems;
        public string details;
    }

    [Serializable]
    class Report
    {
        public string run;
        public string platform;
        public string userAgent;
        public ScenarioResult[] results;
    }

    const float Stall = 3f;     // LogSender.requestTimeout for the test
    const float HardLimit = 30f;

    string origin;
    string run;
    LogSender sender;
    readonly List<ScenarioResult> results = new List<ScenarioResult>();

    IEnumerator Start()
    {
        Application.runInBackground = true;
        origin = Origin(Application.absoluteURL);
        run = Guid.NewGuid().ToString("N").Substring(0, 8);
        // LogSender prefers config.json's logServerUrl once WebSocketManager has loaded it. That is
        // the study server: never send test uploads there.
        if (WebSocketManager.LoadedConfig != null)
        {
            Debug.LogError("[UploadSelfTest] config.json is loaded (study log server); not running.");
            yield break;
        }
        sender = gameObject.AddComponent<LogSender>();
        Debug.Log($"[UploadSelfTest] run {run} against {origin}");

        string big = GamePayload("big", "final", 0, 30000);           // about the size of a full session
        string noisy = RandomPayload("noisy", 6 * 1024 * 1024);       // gzip cannot shrink this

        yield return Scenario("final upload: gzipped, confirmed on the first attempt", big, "final", 0, "",
            (r, p) =>
            {
                Expect(p, sender.FinalUploadConfirmed, "not confirmed: " + sender.LastStatusMessage);
                Expect(p, sender.Attempt == 1, "attempts: " + sender.Attempt);
                Expect(p, sender.LastAttemptCompressed, "not compressed");
                Expect(p, sender.BytesTotal < Encoding.UTF8.GetByteCount(big) / 3, $"sent {sender.BytesTotal} of {Encoding.UTF8.GetByteCount(big)} bytes");
                r.details += $"; gzip {Encoding.UTF8.GetByteCount(big)} -> {sender.BytesTotal} bytes";
            });

        yield return Scenario("same upload again (new job): server answers duplicate, still confirmed", big, "final", 0, "",
            (r, p) => Expect(p, sender.FinalUploadConfirmed && sender.LastStatusMessage.Contains("\"duplicate\": true"),
                             "not confirmed as duplicate: " + sender.LastStatusMessage));

        yield return Scenario("day checkpoint: confirmed", GamePayload("cp", "checkpoint", 3, 2000), "checkpoint", 3, "",
            (r, p) => Expect(p, sender.CurrentStatus == LogSender.SendStatus.Success, "status " + sender.CurrentStatus + ": " + sender.LastStatusMessage));

        yield return Scenario("stalled upload: abandoned, retried, confirmed", noisy, "final", 0, "fault=fail_first&mode=stall&after=65536",
            (r, p) => ExpectRetriedThenConfirmed(r, p, "stopped moving"));

        yield return Scenario("no reply: abandoned, retried, confirmed", GamePayload("hang", "final", 0, 500), "final", 0, "fault=fail_first&mode=hang",
            (r, p) => ExpectRetriedThenConfirmed(r, p, "no reply"));

        yield return Scenario("HTTP 503: retried, confirmed", GamePayload("e503", "final", 0, 500), "final", 0, "fault=fail_first&mode=status503",
            (r, p) => ExpectRetriedThenConfirmed(r, p, "HTTP 503"));

        yield return Scenario("dropped connection: retried, confirmed", noisy, "final", 0, "fault=fail_first&mode=reset&after=65536",
            (r, p) => ExpectRetriedThenConfirmed(r, p, "connection was lost"));

        yield return Scenario("HTTP 413: given up without retrying", GamePayload("e413", "final", 0, 100), "final", 0, "fault=status&code=413",
            (r, p) =>
            {
                Expect(p, sender.FinalUploadGaveUp && !sender.FinalUploadConfirmed, "did not give up");
                Expect(p, sender.Attempt == 1, "attempts: " + sender.Attempt);
            });

        yield return Scenario("hard limit: given up without retrying", noisy, "final", 0, "fault=slow&rate=150000",
            (r, p) =>
            {
                Expect(p, sender.FinalUploadGaveUp, "did not give up");
                Expect(p, sender.Attempt == 1, "attempts: " + sender.Attempt);
                Expect(p, sender.LastFailureReason.Contains("longer than"), "reason: " + sender.LastFailureReason);
            }, hardLimit: 5f);

        yield return Scenario("pre-gzip server: resent uncompressed, confirmed", GamePayload("old", "final", 0, 500), "final", 0, "fault=old_server",
            (r, p) =>
            {
                Expect(p, sender.FinalUploadConfirmed, "not confirmed: " + sender.LastStatusMessage);
                Expect(p, sender.Attempt == 2, "attempts: " + sender.Attempt);
                Expect(p, !sender.LastAttemptCompressed, "last attempt still compressed");
            });

        yield return Scenario("wrong ack byte count: never confirmed", GamePayload("ack", "final", 0, 100), "final", 0, "fault=bad_ack",
            (r, p) =>
            {
                Expect(p, sender.FinalUploadGaveUp && !sender.FinalUploadConfirmed, "was confirmed");
                Expect(p, sender.Attempt == 3, "attempts: " + sender.Attempt);
                Expect(p, sender.LastFailureReason.Contains("did not confirm"), "reason: " + sender.LastFailureReason);
            });

        yield return SupersedeScenario(noisy);
        yield return EndScreenScenario("end-of-game screen: progress text and slider, green success, Back to Survey", noisy, "fault=slow&rate=1500000", true);
        yield return EndScreenScenario("end-of-game screen: red failure with ID, slider hidden, Back to Survey", GamePayload("e413b", "final", 0, 100), "fault=status&code=413", false);

        yield return PostReport();
    }

    // ── scenarios ───────────────────────────────────────────────────────────────────────────

    IEnumerator Scenario(string name, string json, string kind, int day, string fault,
                         Action<ScenarioResult, List<string>> check, float hardLimit = HardLimit)
    {
        var r = new ScenarioResult { name = name };
        var p = new List<string>();
        Configure(fault, hardLimit);
        lastFailureSeen = "";

        float frameGapMax = 0f;
        float t0 = Time.realtimeSinceStartup;
        float startCall = Time.realtimeSinceStartup;
        sender.StartJob(kind, day, json.Replace("RUNID", run));
        float startCost = Time.realtimeSinceStartup - startCall;

        var phases = new List<string>();
        int progressSteps = 0;
        long lastSent = -1;
        float last = Time.realtimeSinceStartup;
        while (sender.ActiveKind != null && Time.realtimeSinceStartup - t0 < 120f)
        {
            yield return null;
            float now = Time.realtimeSinceStartup;
            frameGapMax = Mathf.Max(frameGapMax, now - last);
            last = now;
            string phase = sender.Phase.ToString();
            if (phases.Count == 0 || phases[phases.Count - 1] != phase) phases.Add(phase);
            if (sender.Phase == LogSender.UploadPhase.Uploading && sender.BytesSent != lastSent)
            {
                lastSent = sender.BytesSent;
                progressSteps++;
            }
        }
        if (sender.ActiveKind != null) p.Add("still running after 120 s");

        r.details = $"{Time.realtimeSinceStartup - t0:F1}s, attempts {sender.Attempt}, phases {string.Join(">", phases)}, " +
                    $"{progressSteps} progress steps, StartJob {startCost * 1000:F0} ms, longest frame {frameGapMax * 1000:F0} ms, " +
                    $"last: {Truncate(sender.LastStatusMessage, 140)}";
        check(r, p);
        Finish(r, p);
    }

    void ExpectRetriedThenConfirmed(ScenarioResult r, List<string> p, string firstFailure)
    {
        Expect(p, sender.FinalUploadConfirmed, "not confirmed: " + sender.LastStatusMessage);
        Expect(p, sender.Attempt == 2, "attempts: " + sender.Attempt);
        Expect(p, r.details.Contains("WaitingToRetry"), "never waited to retry");
        Expect(p, lastFailureSeen.Contains(firstFailure), $"first failure was \"{lastFailureSeen}\", expected \"{firstFailure}\"");
    }

    // LogSender clears LastFailureReason once an attempt is confirmed; keep the last one seen.
    string lastFailureSeen = "";

    void Update()
    {
        if (sender != null && !string.IsNullOrEmpty(sender.LastFailureReason))
            lastFailureSeen = sender.LastFailureReason;
    }

    IEnumerator SupersedeScenario(string json)
    {
        var r = new ScenarioResult { name = "final upload replaces a checkpoint still uploading" };
        var p = new List<string>();
        Configure("fault=slow&rate=200000", HardLimit);
        sender.StartJob("checkpoint", 5, json.Replace("RUNID", run + "s"));
        float t0 = Time.realtimeSinceStartup;
        while (sender.Phase != LogSender.UploadPhase.Uploading && Time.realtimeSinceStartup - t0 < 10f) yield return null;
        yield return new WaitForSecondsRealtime(1f);
        Expect(p, sender.ActiveKind == "checkpoint", "checkpoint not running: " + sender.ActiveKind);

        Configure("", HardLimit);
        sender.StartJob("final", 0, GamePayload("sup", "final", 0, 500).Replace("RUNID", run));
        t0 = Time.realtimeSinceStartup;
        while (sender.ActiveKind != null && Time.realtimeSinceStartup - t0 < 60f) yield return null;
        Expect(p, sender.FinalUploadConfirmed, "final not confirmed: " + sender.LastStatusMessage);
        r.details = $"final confirmed after {Time.realtimeSinceStartup - t0:F1}s";
        Finish(r, p);
    }

    IEnumerator EndScreenScenario(string name, string json, string fault, bool succeeds)
    {
        var r = new ScenarioResult { name = name };
        var p = new List<string>();
        Configure(fault, HardLimit);
        lastFailureSeen = "";

        // A bare-bones copy of the Day 8 screen's save indicator, text, progress slider and continue button.
        var canvasGo = new GameObject("SelfTestCanvas", typeof(Canvas));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var textGo = new GameObject("SavingText", typeof(RectTransform));
        textGo.transform.SetParent(canvasGo.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = "Saving your data, please wait.";
        var buttonGo = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(canvasGo.transform, false);
        var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderGo.transform.SetParent(canvasGo.transform, false);
        var slider = sliderGo.GetComponent<Slider>();

        var panelGo = new GameObject("EndOfGamePanel");
        panelGo.SetActive(false);
        var panel = panelGo.AddComponent<EndOfGamePanel>();
        panel.savingDataIndicator = textGo;
        panel.savingDataText = text;
        panel.continueButton = buttonGo.GetComponent<Button>();
        panel.uploadProgressSlider = slider;
        panel.followUpSurveyUrlTemplate = "";
        panelGo.SetActive(true);
        yield return null;   // Start() hides the button, indicator and slider
        Expect(p, !sliderGo.activeSelf && !textGo.activeSelf && !buttonGo.activeSelf, "slider, text or button visible before the upload");
        Expect(p, !slider.interactable, "slider can be dragged");

        sender.StartJob("final", 0, json.Replace("RUNID", run + "e"));
        panel.ShowPanel();

        var seen = new List<string>();
        var sliderValues = new List<float>();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            if (seen.Count == 0 || seen[seen.Count - 1] != text.text) seen.Add(text.text);
            if (sliderGo.activeSelf && (sliderValues.Count == 0 || sliderValues[sliderValues.Count - 1] != slider.normalizedValue))
                sliderValues.Add(slider.normalizedValue);
            if (buttonGo.activeSelf) break;
            yield return new WaitForSecondsRealtime(0.1f);
        }
        if (seen[seen.Count - 1] != text.text) seen.Add(text.text);

        Expect(p, buttonGo.activeSelf, "Back to Survey button not shown");
        Expect(p, textGo.activeSelf, "saving text hidden at the end");
        if (succeeds)
        {
            Expect(p, text.text == "Data Upload Success. You can go back to the survey now.", "success text: " + text.text);
            Expect(p, text.color == panel.successTextColor, "success text color " + text.color);
            Expect(p, seen.Exists(s => s.Contains("Uploading your game data:") && s.Contains("%") && s.Contains("keep this window open")),
                   "no progress text");
            Expect(p, seen.FindAll(s => s.Contains("Uploading your game data:")).Count >= 3, "progress text did not update");
            Expect(p, sliderGo.activeSelf && Mathf.Approximately(slider.normalizedValue, 1f), "slider not full at the end");
            Expect(p, sliderValues.FindAll(v => v > 0f && v < 1f).Count >= 3, "slider did not move through the upload");
        }
        else
        {
            Expect(p, text.text.StartsWith("We couldn't confirm that your game data was saved. Please contact the researcher for assistance. Your "),
                   "failure text: " + text.text);
            Expect(p, text.color == panel.failureTextColor, "failure text color " + text.color);
            Expect(p, !sliderGo.activeSelf, "slider still shown after the failure");
        }
        r.details = $"{seen.Count} distinct texts, {sliderValues.Count} slider values; first: \"{Truncate(seen[0], 80)}\"; " +
                    $"last: \"{Truncate(text.text, 160)}\" ({ColorUtility.ToHtmlStringRGB(text.color)})";
        Finish(r, p);

        Destroy(panelGo);
        Destroy(canvasGo);
        yield return null;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    void Configure(string fault, float hardLimit)
    {
        string url = origin + "/save?run=" + run + (string.IsNullOrEmpty(fault) ? "" : "&" + fault);
        sender.ConfigureForSelfTest(url, Stall, hardLimit, new[] { 1f, 1f }, new[] { 1f }, true);
    }

    static void Expect(List<string> problems, bool ok, string what)
    {
        if (!ok) problems.Add(what);
    }

    void Finish(ScenarioResult r, List<string> p)
    {
        r.pass = p.Count == 0;
        r.problems = string.Join(" | ", p);
        results.Add(r);
        Debug.Log($"[UploadSelfTest] {(r.pass ? "PASS" : "FAIL")} {r.name}: {r.problems} ({r.details})");
    }

    IEnumerator PostReport()
    {
        var report = new Report
        {
            run = run,
            platform = Application.platform.ToString(),
            userAgent = SystemInfo.operatingSystem,
            results = results.ToArray(),
        };
        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(report));
        using (var request = new UnityWebRequest(origin + "/report", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
        }
    }

    static string Origin(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.GetLeftPart(UriPartial.Authority);
        }
        catch (UriFormatException)
        {
            return "http://127.0.0.1:8770";
        }
    }

    static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max) + "...";

    // A payload shaped like GameLogPanel's export: repetitive log rows and JSON-in-JSON deltas.
    // RUNID is replaced per scenario, so each scenario gets its own files on the server.
    static string GamePayload(string tag, string kind, int day, int rows)
    {
        var sb = new StringBuilder(rows * 420);
        sb.Append("{\"sessionId\":\"").Append(tag).Append("RUNID\",\"playerName\":\"webgl\",\"gameVersion\":\"selftest\",")
          .Append("\"uploadKind\":\"").Append(kind).Append("\",\"checkpointDay\":").Append(day)
          .Append(",\"totalMessages\":").Append(rows).Append(",\"messages\":[");
        for (int i = 0; i < rows; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"timestamp\":").Append((100 + i * 0.37f).ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"day\":").Append(1 + i / 4000)
              .Append(",\"round\":").Append(1 + i % 4).Append(",\"budget\":").Append(8000 - i % 300 * 7)
              .Append(",\"satisfaction\":").Append(55 + i % 17).Append(",\"efficiency\":").Append(61 + i % 11)
              .Append(",\"stateSummary\":\"").Append(i % 9 == 0 ? "facilities=[Kitchen Alpha:food=" + (i % 200) + ",clients=0/0,status=InUse;Shelter Beta:food=" + (i % 90) + ",clients=" + (i % 40) + "/100,status=InUse] | selfWalking=" + (i % 3) + " | tasks=[Food Request] | flood=" + (i % 25) : "")
              .Append("\",\"category\":\"Player\",\"messageType\":\"Normal\",\"content\":\"Vehicle ").Append(1 + i % 3)
              .Append(" delivered ").Append(i % 50).Append(" food packs to Shelter Beta (task ").Append(i).Append(")\",")
              .Append("\"stateDeltaJson\":\"{\\\"baseSeq\\\":").Append(i - 1).Append(",\\\"ops\\\":[{\\\"op\\\":\\\"replace\\\",\\\"path\\\":\\\"/economy/currentBudget\\\",\\\"value\\\":")
              .Append(8000 - i).Append("}]}\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // A payload gzip cannot shrink, so the bytes on the wire are predictable.
    static string RandomPayload(string tag, int size)
    {
        var bytes = new byte[size * 3 / 4];
        var random = new System.Random(1);
        random.NextBytes(bytes);
        return "{\"sessionId\":\"" + tag + "RUNID\",\"playerName\":\"webgl\",\"uploadKind\":\"final\",\"totalMessages\":1," +
               "\"messages\":[{\"content\":\"" + Convert.ToBase64String(bytes) + "\"}]}";
    }
}
#endif
