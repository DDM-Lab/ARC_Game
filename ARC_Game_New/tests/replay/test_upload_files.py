"""Upload-handling tests for save_game_logs.py: what gets saved, when the reply goes out, and how
resends and the background replay build behave.

Run from the repository root:

    python -m unittest discover -s tests/replay -v
"""

import contextlib
import gzip
import io
import json
import os
import shutil
import sys
import tempfile
import time
import unittest
from unittest import mock

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, REPO)
sys.path.insert(0, HERE)

import save_game_logs as sgl  # noqa: E402
from test_deltas import _chain  # noqa: E402


def _body(payload, gzipped=False):
    """The request body a client sends: the payload JSON, gzipped by newer clients."""
    data = json.dumps(payload).encode("utf-8")
    return gzip.compress(data) if gzipped else data


def _payload(n_messages, kind="final", day=0):
    payload, _ = _chain()
    payload.update({
        "sessionId": "s1",
        "playerName": "p1",
        "gameVersion": "test",
        "uploadKind": kind,
        "checkpointDay": day,
        "totalMessages": n_messages,
        "messages": [{"timestamp": i, "day": 8, "round": 4, "content": "row {}".format(i)}
                     for i in range(n_messages)],
    })
    return payload


class UploadTestCase(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp()
        self.log_dir = os.path.join(self.root, "arc-game")
        self.checkpoint_dir = os.path.join(self.log_dir, "checkpoints")
        self.spawned = []
        for target, value in (("LOG_DIR", self.log_dir), ("CHECKPOINT_DIR", self.checkpoint_dir),
                              ("spawn_replay_build", self.spawned.append)):
            patcher = mock.patch.object(sgl, target, value)
            patcher.start()
            self.addCleanup(patcher.stop)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def post(self, payload, gzipped=False, query=""):
        """Runs one CGI request in-process. Returns (reply, request body bytes)."""
        reply, body, _ = self.post_raw(_body(payload, gzipped), query)
        return reply, body

    def post_raw(self, body, query=""):
        """Returns (reply, body, HTTP status)."""
        env = {"REQUEST_METHOD": "POST", "CONTENT_LENGTH": str(len(body)), "QUERY_STRING": query}
        out = io.StringIO()
        stdin = io.TextIOWrapper(io.BytesIO(body))
        with mock.patch.dict(os.environ, env), mock.patch.object(sys, "stdin", stdin), \
                contextlib.redirect_stdout(out):
            sgl.main()
        head, reply = out.getvalue().split("\n\n", 1)
        status = int(head.split("\n", 1)[0].split()[1])
        return json.loads(reply), body, status

    def upload_log(self, base="p1_s1_final", directory=None):
        with open(self.path(base + sgl.UPLOAD_LOG_SUFFIX, directory), encoding="utf-8") as f:
            return [json.loads(line) for line in f]

    def path(self, name, directory=None):
        return os.path.join(directory or self.log_dir, name)

    def csv_files(self, directory=None):
        return sorted(n for n in os.listdir(directory or self.log_dir) if n.endswith(".csv"))


class SavingAndReply(UploadTestCase):
    def test_final_upload_is_saved_and_acked_before_any_replay_work(self):
        reply, body = self.post(_payload(5))
        self.assertTrue(reply["ok"])
        self.assertEqual(reply["bytes"], len(body))
        self.assertEqual(reply["messages"], 5)
        self.assertEqual(reply["file"], "p1_s1_final.csv")
        self.assertTrue(reply["replay_pending"])
        self.assertEqual(sgl.saved_message_count(self.path("p1_s1_final.csv")), 5)
        with open(self.path("p1_s1_final" + sgl.RAW_SUFFIX), "rb") as f:
            self.assertEqual(gzip.decompress(f.read()), body)
        self.assertFalse(os.path.exists(self.path("p1_s1_final" + sgl.REPLAY_SUFFIX)))
        self.assertEqual(self.spawned, [self.path("p1_s1_final" + sgl.RAW_SUFFIX)])

    def test_resend_replaces_the_earlier_copy_instead_of_adding_a_file(self):
        self.post(_payload(5))
        reply, _ = self.post(_payload(7))
        self.assertTrue(reply["replaced_earlier_copy"])
        self.assertEqual(self.csv_files(), ["p1_s1_final.csv"])
        self.assertEqual(sgl.saved_message_count(self.path("p1_s1_final.csv")), 7)

    def test_older_resend_arriving_late_does_not_overwrite_the_newer_copy(self):
        self.post(_payload(7))
        reply, _ = self.post(_payload(5))
        self.assertTrue(reply["ok"])  # its data is already in the saved copy
        self.assertEqual(reply["result"], "kept_newer")
        self.assertEqual(sgl.saved_message_count(self.path("p1_s1_final.csv")), 7)
        self.assertEqual(len(self.spawned), 1)

    def test_new_copy_removes_the_sidecar_of_the_old_one(self):
        self.post(_payload(5))
        for suffix in (sgl.REPLAY_SUFFIX, sgl.REPLAY_ERROR_SUFFIX):
            with open(self.path("p1_s1_final" + suffix), "w") as f:
                f.write("stale")
        self.post(_payload(7))
        for suffix in (sgl.REPLAY_SUFFIX, sgl.REPLAY_ERROR_SUFFIX):
            self.assertFalse(os.path.exists(self.path("p1_s1_final" + suffix)))

    def test_checkpoint_keeps_its_per_day_name(self):
        reply, _ = self.post(_payload(4, kind="checkpoint", day=3))
        self.assertEqual(reply["file"], "p1_s1_checkpoint_day3.csv")
        self.assertEqual(self.csv_files(self.checkpoint_dir), ["p1_s1_checkpoint_day3.csv"])
        self.assertEqual(self.csv_files(), [])


class GzipUploads(UploadTestCase):
    def test_gzipped_upload_is_stored_as_received_and_acked_with_the_compressed_size(self):
        payload = _payload(5)
        reply, body = self.post(payload, gzipped=True)
        plain = json.dumps(payload).encode("utf-8")
        self.assertTrue(reply["ok"])
        self.assertTrue(reply["compressed"])
        self.assertEqual(reply["bytes"], len(body))         # what the client sent: compressed
        self.assertEqual(reply["payload_bytes"], len(plain))
        with open(self.path("p1_s1_final" + sgl.RAW_SUFFIX), "rb") as f:
            self.assertEqual(f.read(), body)                # not recompressed
        self.assertEqual(sgl.saved_message_count(self.path("p1_s1_final.csv")), 5)

    def test_gzip_and_plain_uploads_save_the_same_csv(self):
        self.post(_payload(5))
        with open(self.path("p1_s1_final.csv"), "rb") as f:
            plain_csv = f.read()
        shutil.rmtree(self.log_dir)
        self.post(_payload(5), gzipped=True)
        with open(self.path("p1_s1_final.csv"), "rb") as f:
            self.assertEqual(f.read(), plain_csv)

    def test_plain_upload_still_reports_its_own_size(self):
        reply, body = self.post(_payload(5))
        self.assertFalse(reply["compressed"])
        self.assertEqual(reply["bytes"], len(body))
        self.assertEqual(reply["payload_bytes"], len(body))

    def test_corrupt_gzip_is_refused(self):
        body = bytearray(_body(_payload(5), gzipped=True))
        body[len(body) // 2] ^= 0xFF
        reply, _, status = self.post_raw(bytes(body))
        self.assertEqual(status, 400)
        self.assertFalse(reply["ok"])
        self.assertFalse(os.path.exists(self.path("p1_s1_final.csv")))

    def test_truncated_gzip_is_refused(self):
        body = _body(_payload(5), gzipped=True)
        reply, _, status = self.post_raw(body[:len(body) - 20])
        self.assertEqual(status, 400)
        self.assertIn("gzip", reply["message"])

    def test_decompressed_size_is_capped(self):
        bomb = gzip.compress(b" " * 5000)  # 5 kB of JSON whitespace from ~40 bytes
        with mock.patch.object(sgl, "MAX_PAYLOAD_BYTES", 1000):
            reply, _, status = self.post_raw(bomb)
        self.assertEqual(status, 413)
        self.assertFalse(reply["ok"])

    def test_payload_at_the_cap_is_accepted(self):
        body = _body(_payload(5), gzipped=True)
        size = len(json.dumps(_payload(5)).encode("utf-8"))
        with mock.patch.object(sgl, "MAX_PAYLOAD_BYTES", size):
            reply, _, status = self.post_raw(body)
        self.assertEqual(status, 200, reply)


class Duplicates(UploadTestCase):
    def test_identical_resend_is_acked_without_rewriting_or_rebuilding(self):
        payload = _payload(5)
        self.post(payload, gzipped=True, query="uploadId=abc&attempt=1")
        stat_before = os.stat(self.path("p1_s1_final.csv"))
        reply, body = self.post(payload, gzipped=True, query="uploadId=abc&attempt=2")
        self.assertTrue(reply["ok"])
        self.assertTrue(reply["duplicate"])
        self.assertEqual(reply["bytes"], len(body))
        self.assertEqual(len(self.spawned), 1)  # no second replay build
        self.assertEqual(os.stat(self.path("p1_s1_final.csv")).st_mtime_ns, stat_before.st_mtime_ns)
        log = self.upload_log()
        self.assertEqual([(e["result"], e["attempt"], e["uploadId"]) for e in log],
                         [("saved", 1, "abc"), ("duplicate", 2, "abc")])

    def test_same_payload_plain_then_gzipped_is_a_duplicate(self):
        payload = _payload(5)
        self.post(payload)
        reply, _ = self.post(payload, gzipped=True)
        self.assertTrue(reply["duplicate"])

    def test_same_message_count_with_different_content_replaces(self):
        first = _payload(5)
        second = _payload(5)
        second["messages"][4]["content"] = "changed"
        self.post(first)
        reply, _ = self.post(second)
        self.assertEqual(reply["result"], "saved")
        self.assertEqual(len(self.spawned), 2)

    def test_upload_log_records_every_request(self):
        self.post(_payload(5), gzipped=True, query="uploadId=u1&attempt=1")
        self.post(_payload(4))
        log = self.upload_log()
        self.assertEqual([e["result"] for e in log], ["saved", "kept_newer"])
        self.assertTrue(log[0]["compressed"])
        self.assertEqual(log[0]["messages"], 5)
        self.assertIsNone(log[1]["attempt"])  # an older client sends no query string


class ReplayBuild(UploadTestCase):
    def raw_path(self):
        return self.path("p1_s1_final" + sgl.RAW_SUFFIX)

    def test_build_writes_the_same_sidecar_the_request_used_to(self):
        payload = _payload(5)
        self.post(payload)
        self.assertEqual(sgl.build_replay(self.raw_path()), "built")
        with open(self.path("p1_s1_final" + sgl.REPLAY_SUFFIX), encoding="utf-8") as f:
            sidecar = json.load(f)
        self.assertEqual(sidecar, json.loads(sgl.build_replay_sidecar(payload).decode("utf-8")))
        self.assertTrue(sidecar["replayVerification"]["ok"])

    def test_build_for_a_replaced_upload_does_not_write_old_data(self):
        self.post(_payload(5))
        real_build = sgl.build_replay_sidecar

        def newer_upload_lands_meanwhile(*args):
            data = real_build(*args)
            self.post(_payload(7))
            return data

        with mock.patch.object(sgl, "build_replay_sidecar", newer_upload_lands_meanwhile):
            self.assertEqual(sgl.build_replay(self.raw_path()), "superseded")
        self.assertFalse(os.path.exists(self.path("p1_s1_final" + sgl.REPLAY_SUFFIX)))

    def test_failed_build_leaves_a_reason(self):
        os.makedirs(self.log_dir)
        with open(self.raw_path(), "wb") as f:
            f.write(b"not gzip")
        self.assertEqual(sgl.build_replay(self.raw_path()), "failed")
        self.assertTrue(os.path.exists(self.path("p1_s1_final" + sgl.REPLAY_ERROR_SUFFIX)))
        self.assertFalse(os.path.exists(self.path("p1_s1_final" + sgl.REPLAY_SUFFIX)))

    def test_build_pending_builds_only_what_is_missing(self):
        self.post(_payload(5))
        self.post(_payload(4, kind="checkpoint", day=2))
        self.assertEqual(sgl.build_pending()["built"], 2)
        self.assertEqual(sgl.build_pending()["built"], 0)

    def test_build_of_a_gzipped_upload(self):
        payload = _payload(5)
        self.post(payload, gzipped=True)
        self.assertEqual(sgl.build_replay(self.raw_path()), "built")
        with open(self.path("p1_s1_final" + sgl.REPLAY_SUFFIX), encoding="utf-8") as f:
            self.assertTrue(json.load(f)["replayVerification"]["ok"])

    def test_second_build_of_the_same_upload_is_skipped(self):
        self.post(_payload(5))
        self.assertEqual(sgl.build_replay(self.raw_path()), "built")
        with mock.patch.object(sgl, "build_replay_sidecar") as build:
            self.assertEqual(sgl.build_replay(self.raw_path()), "up_to_date")
        build.assert_not_called()

    def test_build_results_are_in_the_upload_log(self):
        self.post(_payload(5))
        sgl.build_replay(self.raw_path())
        self.assertEqual([(e["event"], e["result"]) for e in self.upload_log()],
                         [("upload", "saved"), ("replay", "built")])


class CommandLine(UploadTestCase):
    def test_refused_when_run_as_cgi(self):
        self.post(_payload(5))
        out = io.StringIO()
        with mock.patch.dict(os.environ, {"GATEWAY_INTERFACE": "CGI/1.1"}), contextlib.redirect_stdout(out):
            self.assertEqual(sgl.cli(["--build-pending"]), 2)
        self.assertIn('"ok": false', out.getvalue())
        self.assertFalse(os.path.exists(self.path("p1_s1_final" + sgl.REPLAY_SUFFIX)))

    def test_build_replay_only_accepts_uploads_inside_the_log_dir(self):
        outside = os.path.join(self.root, "elsewhere" + sgl.RAW_SUFFIX)
        with mock.patch.dict(os.environ, {"GATEWAY_INTERFACE": ""}), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(sgl.cli(["--build-replay", outside]), 2)
            self.assertEqual(sgl.cli(["--build-replay", self.path("x.csv")]), 2)


class BackgroundProcess(UploadTestCase):
    def test_spawned_process_builds_the_sidecar(self):
        self.post(_payload(5))
        raw = self.path("p1_s1_final" + sgl.RAW_SUFFIX)
        sidecar = self.path("p1_s1_final" + sgl.REPLAY_SUFFIX)
        # The child reads LOG_DIR from ARC_LOG_DIR, not from this process's patched module.
        # GATEWAY_INTERFACE is set as it is under Apache: the child must not inherit it, or
        # cli() would refuse to run.
        with mock.patch.dict(os.environ, {"ARC_LOG_DIR": self.log_dir, "GATEWAY_INTERFACE": "CGI/1.1"}):
            self.assertTrue(_real_spawn(raw))
        deadline = time.time() + 60
        while not os.path.exists(sidecar) and time.time() < deadline:
            time.sleep(0.2)
        self.assertTrue(os.path.exists(sidecar), "background build did not write the sidecar")
        self.assertFalse(os.path.exists(self.path("p1_s1_final" + sgl.REPLAY_ERROR_SUFFIX)))


# The unpatched spawn function, captured before any test patches the module.
_real_spawn = sgl.spawn_replay_build


if __name__ == "__main__":
    unittest.main()
