# Reliable log uploads

How the game's end-of-game and day-checkpoint log uploads work, how to deploy them, how they were tested, and what is still untested.

**Status:** implemented and tested on three levels: server unit tests, the browser plugin in headless Chrome and Edge, and a real WebGL build of a test scene (see [Test results](#test-results)). The full game has not been rebuilt and played through Day 8 with these changes. Do that before the next study session: [Manual checks](#manual-checks-in-the-real-game-build).

New files to commit with this change: `Assets/Plugins/WebGL/CoraUpload.jslib`, `Assets/Scripts/GameLog/UploadSelfTest.cs` and `Assets/Scripts/Editor/UploadSelfTestBuild.cs`, each with the `.meta` file Unity generated, plus `tests/upload_browser/` and `tests/replay/test_upload_files.py`.

## Why this changed

Since Oct 7 each upload carries a replay record of the session, so a finished game uploads about 13 MB of JSON. The server saved each upload but replied too late:

- The game build gave up after 30 s. The serialized scene value overrode the script's 120 s default.
- The server ran the replay check before replying.

So the game resent the full log 5 times and then told the participant to contact the researcher, and the server kept 5 copies.

## How it works now

### Game side

[LogSender.cs](../Assets/Scripts/GameLog/LogSender.cs) runs one upload *job* at a time:

- **Day 1-7:** a cumulative checkpoint (`kind=checkpoint`).
- **Day 8:** the final upload (`kind=final`).
- **Debug panel:** a manual send.

A job builds the JSON **once**, when it is requested, and resends the same bytes on every retry.

Each attempt works like this:

| | WebGL build | Editor / desktop |
|---|---|---|
| Sent by | [CoraUpload.jslib](../Assets/Plugins/WebGL/CoraUpload.jslib) (`XMLHttpRequest`) | `UnityWebRequest` |
| Compression | the browser's `CompressionStream('gzip')`. This is native code and doesn't block the game. Without it, the plain JSON is sent. | `GZipStream` |
| Body | `Content-Type: application/gzip` (not `Content-Encoding`, which a proxy might try to decode) | same |
| Progress | real upload progress from the browser | `UnityWebRequest.uploadedBytes` |
| Stall timeout | no upload progress, or no reply once the body is sent, for `requestTimeout` (**120 s**). The timer runs in the browser, so it still fires while the tab is in the background. | same rule, checked each frame |
| Hard limit | `maxAttemptSeconds` (**600 s**) per attempt, even while the upload is still moving | `UnityWebRequest.timeout` |

An attempt counts as saved only when the reply is the server's ack `{"ok": true, "bytes": N, "payload_bytes": M}`:

- `N` must equal the bytes sent. For a gzipped body that is the compressed size.
- `M` must equal the JSON's size. An older server that doesn't send `M` is accepted.

C# reads the browser plugin's state every frame instead of receiving callbacks. A `SendMessage` callback would depend on the name of the GameObject `LogSender` sits on (`DebugUI`), or on the page defining `window.unityInstance`, which the `ARC` template doesn't. Polling uses only what `BrowserRedirect.jslib` already uses.

### Retries, and whether to retry less after a timeout

| Failure | Retried? | Why |
|---|---|---|
| Stalled (no progress for 120 s), no reply, dropped connection, HTTP 5xx, unconfirmed reply | **Yes** | Usually temporary. A stall now means the connection really stopped, not just that the upload was slow, so another attempt can succeed. |
| Hard limit (600 s) reached while still moving | **No** | The next attempt would be just as slow. |
| HTTP 413 (over the server's size cap) | **No** | Resending the same payload is refused the same way. |
| HTTP 400 "not valid UTF-8" / "Invalid JSON" to a gzip body | Yes, **uncompressed** | A server that predates gzip support. |

Before this change, "fewer retries after a timeout" mattered because the timeout covered the *whole* attempt. A slow connection then timed out on every retry, and each retry restarted from zero. Timeouts now fire only on a stall, so a slow connection no longer hits them. The one deterministic timeout left, the hard limit, is not retried at all.

**Defaults** (serialized fields on `LogSender`):

- **Final upload:** `finalRetryDelaysSeconds = {5, 15, 30}`, so up to 4 attempts. Delays count from the end of the failed attempt.
- **Checkpoints:** `checkpointRetryDelaysSeconds = {30, 90}`, so up to 3 attempts. These run in the background during play.
- **Replacement:** a newer checkpoint, or the final upload, replaces a checkpoint that is still running. Nothing replaces a running final upload.

### End-of-game screen

[EndOfGamePanel.cs](../Assets/Scripts/DailyReport/EndOfGamePanel.cs) shows the final upload's state in the existing "saving" text (`savingDataText`, the object `QualtricsLabelText (1)`). The text box auto-sizes from 36 down to 18 pt. No other text objects are used.

- **Preparing:** "Saving your game data. Please keep this window open."
- **Uploading:** "Uploading your game data: 45% (1.2 of 2.6 MB). Please keep this window open. On a slow connection this can take a few minutes."
- **Waiting for the server:** "Upload finished. Waiting for the server to confirm your data."
- **Waiting to retry:** "The upload did not go through (*reason*). Trying again in 12 s (attempt 2 of 4). Please keep this window open."
- **Confirmed**, in dark green (`successTextColor`, #006400): "Data Upload Success. You can go back to the survey now."
- **Gave up**, in dark red (`failureTextColor`, #8B0000): "We couldn't confirm that your game data was saved. Please contact the researcher for assistance. Your participant ID is *764078*." Without a participant ID, the last sentence names the session ID instead.

The **Back to Survey** button (`continueButton`) appears in both outcomes. The text stays on screen in both.

**Progress bar:** assign a UI `Slider` to `uploadProgressSlider` on the `EndOfGamePanel` component. The script sets it to non-interactable and fills it with the upload's progress (it uses `normalizedValue`, so any min/max works). It is hidden until the Day 8 upload starts, left full on success, and hidden on failure. During a retry wait it shows empty, because a retry starts the upload over.

While the final upload runs, the browser asks "Leave site?" if the page is closed or reloaded. The prompt is removed once the upload is confirmed or given up, so the survey redirect is never interrupted.

### Server side

[save_game_logs.py](../save_game_logs.py):

- **Accepted formats:** plain JSON (older clients) and gzip. Gzip is recognized by its first two bytes, so the `Content-Type` header isn't relied on.
- **Size limits:** 50 MB on the body as received (`MAX_BODY_BYTES`). Gzip bodies are also limited to 50 MB once decompressed (`MAX_PAYLOAD_BYTES`), so a small upload can't expand into gigabytes. Corrupt or truncated gzip gets a 400.
- **What is saved:** for each upload, `<base>.csv` and `<base>.upload.json.gz`. A gzip body is stored byte for byte as received. Then the server replies.
- **One file set per session upload:**
  - `<player>_<session>_final.*` for the final upload;
  - `checkpoints/<player>_<session>_checkpoint_day<N>.*` for checkpoints.
  - A resend replaces the earlier copy, except that a copy with more messages is never replaced.
- **Duplicates:** an upload whose decompressed content is identical to the saved copy gets `"result": "duplicate"`. Nothing is rewritten and no new replay build starts.
- **Received vs. processed:**
  - The ack means the upload is saved ("received").
  - The replay check and `<base>.replay.json` are built afterwards by a background process, one build at a time on the server.
  - If that build fails, `<base>.replay_error.txt` says why. The upload stays saved.
  - `python3 save_game_logs.py --build-pending` builds anything missing.
- **Upload log:** `<base>.upload_log.jsonl` gets one line per request and one per replay build. Each request line records saved / duplicate / kept_newer, the attempt number and upload id from the query string, the compressed and uncompressed sizes, and the browser. Each build line records built / failed / superseded.
- **Query string:** each attempt's URL carries `?kind=final&attempt=2&uploadId=…`, so the web server's access log identifies retries too.

## Configuration changes

| Where | Change |
|---|---|
| `MainScene.unity`, `TutorialScene.unity` | `LogSender.requestTimeout` 30 → **120**. It now means "seconds without progress", not "whole attempt". |
| `LogSender` (new fields; scenes use the defaults) | `maxAttemptSeconds` 600, `compressUploads` true, `finalRetryDelaysSeconds` {5, 15, 30}, `checkpointRetryDelaysSeconds` {30, 90} |
| `EndOfGamePanel` | `retryDelaysSeconds` removed (retries moved to `LogSender`); the prefab's stale value is ignored. New: `uploadProgressSlider` (optional, assign your Slider), `successTextColor` (#006400) and `failureTextColor` (#8B0000). The continue button now also appears when the save fails. |
| `save_game_logs.py` | `MAX_PAYLOAD_BYTES` (50 MB, decompressed). New files per upload: `.upload.json.gz`, `.upload_log.jsonl`, plus `.replay_error.txt` when a build fails. Lock files `.save_game_logs.lock` and `.replay_build.lock` in the log directories. |
| Apache / proxy | **No change needed.** There are no custom headers (the attempt info is in the query string), and the request body gets smaller. If `mod_reqtimeout` is on, its default needs at least 500 bytes/s, far below 0.5 Mbps. |

## Deployment order

1. **Deploy the server script first.** Copy `save_game_logs.py` over the one in the container's `cgi-bin` and keep it executable.
   - Make sure it has **LF line endings**. Git on Windows (`core.autocrlf=true`) may check it out with CRLF, and `#!/usr/bin/env python3\r` won't run as CGI. Check with `file save_game_logs.py` on the server.
   - It accepts both formats, so the live build (plain JSON, 30 s) keeps working.
   - Thanks to the fast reply, most of the live build's uploads should now confirm within 30 s.
2. **Check it as `yuchen`, not with sudo:** run `python3 save_game_logs.py --build-pending`. It should print a summary line, which confirms the script runs and can reach the log directory.
3. After the next real upload, check the log directory:
   - there is one `_final.csv`, `.upload.json.gz` and `.upload_log.jsonl`;
   - a `.replay.json` appears shortly after;
   - the upload log shows `"result": "saved"`.
4. **Only then build and release the game**, with the scene timeouts and gzip. A gzip-sending build against the old server fails its first attempt (400), then resends uncompressed. That works, but it is slower.
5. Before the first study session on the new build, run the [manual checks](#manual-checks-in-the-real-game-build).

Rollback: the previous `save_game_logs.py` cannot read gzip bodies. Don't roll the server back while a gzip-sending build is live.

## Test results

All runs are on Windows 11 on Oct 9, 2026.

### Server: `python -m unittest discover -s tests/replay`

67 tests pass (1 skipped, pre-existing). [test_upload_files.py](../tests/replay/test_upload_files.py) covers:

- gzip and plain uploads;
- ack byte semantics;
- corrupt and truncated gzip;
- the decompressed-size cap (413);
- identical resends (duplicate, no rewrite, no new build);
- older/newer resends;
- per-day checkpoint naming;
- the background build: built, up to date, superseded, failed, and a real spawned process;
- `--build-pending`;
- refusing command-line modes under CGI.

The tests ran on Windows, where the locks are no-ops (no `fcntl`). The `flock` locking itself has not been run on Linux.

### Browser plugin: `python tests/upload_browser/run_browser_tests.py`

The real `CoraUpload.jslib` in headless **Chrome 155** and **Edge 154**, against [fault_server.py](../tests/upload_browser/fault_server.py), with the real `save_game_logs.py` behind it. All 15 scenarios and the server-side check pass in both browsers:

- gzip upload saved and acked with the compressed size;
- identical resend acked as a duplicate;
- plain upload;
- no `CompressionStream`, so it falls back to plain JSON;
- slow upload at 400 KB/s: keeps going, 70+ progress steps;
- stall: abandoned at about 4 s with a 3 s stall timeout;
- no reply: abandoned at about 4 s;
- hard limit (5 s) reached while still moving;
- HTTP 503 passed through;
- dropped connection becomes a network error at once;
- abort;
- wrong ack passed through for C# to reject;
- a server from before gzip support gives a 400;
- leave warning on and off;
- unknown handle.

The server kept one file set for the session, with an upload log of `saved`, then `duplicate`.

### Real WebGL build: Editor/UploadSelfTestBuild.cs + `run_webgl_selftest.py`

This is a real Unity 2022.3.62f3 WebGL development build:
- IL2CPP and Emscripten, with the project's `QualtricsIntegration` template and settings;
- a one-object test scene running [UploadSelfTest.cs](../Assets/Scripts/GameLog/UploadSelfTest.cs) against `fault_server.py`;
- headless Chrome 155.

It exercises the real C# ↔ jslib path (`byte[]` marshalling, polling, release), `LogSender`'s retry policy, and `EndOfGamePanel`'s text. Test timeouts are a 3 s stall and a 30 s hard limit (5 s in the hard-limit scenario), with 1 s retry delays.

```
Unity -batchmode -quit -nographics -projectPath . -buildTarget WebGL -executeMethod UploadSelfTestBuild.Build -selfTestOutput <dir>
python tests/upload_browser/run_webgl_selftest.py --build-dir <dir>
```

| Scenario | Result |
|---|---|
| Final upload, 10.9 MB game-like JSON | Confirmed on attempt 1. Gzipped in the browser to 796 KB (13.7×). |
| Same upload as a new job | Server answers `duplicate`; still confirmed |
| Day checkpoint | Confirmed |
| Upload stalls (server stops reading) | Abandoned at about 3 s, retried, confirmed on attempt 2 |
| No reply after the body is sent | Abandoned, retried, confirmed on attempt 2 |
| HTTP 503 | Retried, confirmed on attempt 2 |
| Dropped connection (TCP reset) | Retried, confirmed on attempt 2 |
| HTTP 413 | Given up after 1 attempt, no retry |
| Hard limit reached while still moving | Given up after 1 attempt, no retry ("took longer than 5 s") |
| Server from before gzip support (400 "not valid UTF-8") | Resent uncompressed, confirmed on attempt 2 |
| Ack with the wrong byte count | Never confirmed; given up after 3 attempts |
| Final upload while a checkpoint is uploading | Checkpoint replaced; final upload confirmed |
| End-of-game screen, slow upload | 46 progress texts ("Uploading your game data: N% …"), then "Waiting for the server…", then the continue button |
| End-of-game screen, failure | Failure text with participant ID; continue button stays hidden |

*After that run, the end-of-game screen changed:* green success text, red failure text, the Back to Survey button also shown on failure, and the progress slider. [UploadSelfTest.cs](../Assets/Scripts/GameLog/UploadSelfTest.cs) checks all of these, but the WebGL build test has **not been re-run since this change**. The Editor had the project open, so a batchmode build wasn't possible. Re-run it before relying on the new screen: in the Editor, **Build → Upload Self-Test (WebGL)**, then `python tests/upload_browser/run_webgl_selftest.py --build-dir Build/UploadSelfTest`.

**Responsiveness:** starting the 10.9 MB upload took 45 ms on the main thread (UTF-8 encoding). The longest frame during any upload was 184 ms, under software rendering. Building the JSON itself (`GameLogPanel.GetMessagesAsJson`) wasn't part of this test; it now runs once per upload instead of once per attempt.

**Found by this test and fixed:**
- `StartJob` didn't reset the final-upload flags. Game code was unaffected, because `SendAllLogs` resets them.
- Chromium resends a POST once, by itself, when a reused keep-alive connection is reset before any reply. A single dropped connection is therefore often recovered by the browser before `LogSender` sees it. The test server now fails every request of an attempt.

**Not covered by this build test:** the "Leave site?" prompt (only the browser test covers it), a hidden or background tab, and the full game scene.

### Compression

- **Real message rows** (4 study sessions from September, which have no replay record): 17–20× at gzip level 6, the browser's default. For example, 1.81 MB → 94 KB.
- **Synthetic game-like payload** in the browser tests: 14×.
- **Full payload:** today's 13 MB payload is mostly replay data, which couldn't be measured locally. The server's upload log records `bytes` and `payloadBytes` for every upload, so the first real uploads give the true ratio.
- **What it means for slow connections:** at 10×, 13 MB becomes 1.3 MB, which takes about 21 s at 0.5 Mbps instead of about 212 s.

## Manual checks in the real game build

These need the full game, which can't be automated here.

1. **Normal run.** Play to Day 8 with `config.json`'s `logServerUrl` pointing at the updated server. You should see the progress text, then the continue button. On the server, the upload log should show `"compressed": true`, and the `.replay.json` should appear.
2. **Slow connection.** In Chrome DevTools → Network, add a custom profile with 0.5 Mbps upload. Reach Day 8: the percentage should keep moving and the upload should complete.
3. **Interruption.** During the Day 8 upload, switch DevTools to Offline for about 2 minutes, then back. You should see "stopped moving", a retry countdown, and then success.
4. **Leave warning.** Try to close the tab during the Day 8 upload. The browser should ask "Leave site?". After success, the continue button's redirect to the survey should go through without a prompt.
5. **Background tab.** Start the Day 8 upload, switch to another tab for a minute, then come back. The upload should have continued.
6. **Checkpoints.** After Day 1–7, the server should show one checkpoint file set per day, with no duplicates.
7. **Other browsers.** Repeat step 1 in Firefox and Safari (macOS). They were not available for testing here.

## Remaining limitations

- **The full game build is not verified.** The C#/browser path was verified in a real WebGL build of a test scene, not in the game itself.
- **Firefox and Safari are untested.** Only Chromium browsers (Chrome, Edge) were tested.
- **The full-payload compression ratio is unknown** until the first real upload; see the server's upload log.
- **The final upload's own attempt rows aren't in its saved data.** The final upload's payload is built when it starts, so its retry rows are not in it. The server's upload log records every attempt instead. Checkpoints' attempt rows do appear in later uploads.
- **Retries pause while the tab is hidden.** An upload already in progress continues in a background tab, and its stall and hard-limit timers still run. But the retry countdown and the progress text run on Unity's frame loop, which browsers pause for hidden tabs.
- **`fcntl` locking is Linux-only.** It is untested on the real server's filesystem. If `/home/ddmlab/project-containers` is a network filesystem without `flock` support, the locks may not exclude each other. They only guard against concurrent resends of the same session, which is rare.
- **The live build won't get the client fixes.** Build 1.0.2 (30 s, plain JSON) only benefits from the server's faster reply. A participant whose 13 MB upload takes over 30 s on a slow connection will still see the warning until the new build is released.
