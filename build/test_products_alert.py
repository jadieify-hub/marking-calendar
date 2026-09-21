"""Failure notifications identify affected groups without sending messages in tests."""
import io
import json
from pathlib import Path
import tempfile
from contextlib import redirect_stderr
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs

from products_alert import current_state, main, messages


class AlertTests(unittest.TestCase):
    def test_state_tracks_only_delivered_transitions_and_preserves_failures_without_full_log(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "assets/groups").mkdir(parents=True)
            groups = [{"name": "Молоко", "link": "/milk/"}, {"name": "Обувь", "link": "/shoes/"}]
            (root / "assets/groups/groups.json").write_text(json.dumps({"groups": groups}), encoding="utf-8")
            (root / "data").mkdir()
            state_path = root / "data/products-alert-state.json"
            env = {"TELEGRAM_BOT_TOKEN": "secret-token", "TELEGRAM_CHAT_ID": "123",
                   "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "owner/repo", "GITHUB_RUN_ID": "1",
                   "PRODUCTS_JOB_RESULT": "failure", "PRODUCTS_CHECK_EXIT_CODE": "2", "PRODUCTS_SOURCE_FAILURE_ONLY": "true"}
            def run(failed, *, partial=False):
                log = "PRODUCTS_FAILED=" + ",".join(failed)
                if not partial:
                    log = "PRODUCTS_CHANGED=\nPRODUCTS_UNCHANGED=" + ",".join(g for g in ["milk", "shoes"] if g not in failed) + "\n" + log
                (root / "products-check.log").write_text(log, encoding="utf-8")
                return main(root)

            with patch.dict("os.environ", env), patch("products_alert.urlopen") as send:
                send.return_value.__enter__.return_value.read.return_value = b'{"ok": true}'
                self.assertEqual(0, run(["milk"]))
                self.assertEqual(1, send.call_count)
                self.assertEqual(["milk"], json.loads(state_path.read_text())["failedGroups"])
                self.assertEqual(0, run(["milk"]))
                self.assertEqual(1, send.call_count, "Identical failures must stay silent")
                self.assertEqual(0, run(["shoes"]))
                body = send.call_args.args[0].data.decode()
                message = parse_qs(body)["text"][0]
                self.assertIn("Обувь", message)
                self.assertIn("Восстановлено", message)
                self.assertIn("Молоко", message)
                before = state_path.read_bytes()
                with patch.dict("os.environ", {"TELEGRAM_BOT_TOKEN": ""}), redirect_stderr(io.StringIO()):
                    count = send.call_count
                    self.assertEqual(1, run(["milk", "shoes"]))
                    self.assertEqual(count, send.call_count)
                    self.assertEqual(before, state_path.read_bytes())
                send.side_effect = OSError("https://api.telegram.org/botsecret-token")
                output = io.StringIO()
                with redirect_stderr(output):
                    self.assertEqual(1, run(["milk", "shoes"]))
                self.assertNotIn("secret-token", output.getvalue())
                self.assertEqual(before, state_path.read_bytes(), "Failed delivery cannot acknowledge changes")
                send.side_effect = None
                self.assertEqual(0, run(["milk", "shoes"]))
                self.assertEqual(["milk", "shoes"], json.loads(state_path.read_text())["failedGroups"])
                self.assertEqual(0, run([], partial=True))
                partial_state = json.loads(state_path.read_text())
                self.assertEqual(["milk", "shoes"], partial_state["failedGroups"])
                self.assertTrue(partial_state["jobFailed"])
                count = send.call_count
                self.assertEqual(0, run([], partial=True))
                self.assertEqual(count, send.call_count, "An unchanged generic failure must stay silent")
                with patch.dict("os.environ", {"PRODUCTS_JOB_RESULT": "success", "PRODUCTS_CHECK_EXIT_CODE": "0"}):
                    self.assertEqual(0, run([]))
                    self.assertEqual({"failedGroups": [], "jobFailed": False}, json.loads(state_path.read_text()))
                    count = send.call_count
                    self.assertEqual(0, run([]))
                    self.assertEqual(count, send.call_count)

    def test_missing_groups_or_failed_collection_cannot_confirm_recovery(self):
        groups = [{"name": "Молоко", "link": "/milk/"}]
        previous = {"failedGroups": ["milk"], "jobFailed": False}
        for log, exit_code in [("PRODUCTS_CHANGED=\nPRODUCTS_UNCHANGED=\nPRODUCTS_FAILED=", "0"),
                               ("PRODUCTS_CHANGED=milk\nPRODUCTS_UNCHANGED=\nPRODUCTS_FAILED=", "4")]:
            with self.subTest(exit_code=exit_code):
                self.assertEqual({"failedGroups": ["milk"], "jobFailed": True},
                                 current_state(log, groups, previous, exit_code, False))

    def test_names_reasons_and_run_link_survive_message_splitting(self):
        groups = [{"name": f"Группа {i}", "link": f"/business/projects/g{i}/"} for i in range(50)]
        log = "\n".join(f"g{i}: Не найдена таблица " + "x" * 100 for i in range(50))
        log += "\nPRODUCTS_FAILED=" + ",".join(f"g{i}" for i in range(50))
        result = messages(log, groups, "https://github.com/owner/repo/actions/runs/1",
                          {"failedGroups": [], "jobFailed": False},
                          {"failedGroups": [f"g{i}" for i in range(50)], "jobFailed": False})
        self.assertTrue(all(len(message) <= 3500 for message in result))
        combined = "\n".join(result)
        for i in range(50):
            self.assertIn(f"Группа {i}:", combined)
        self.assertIn("Не найдена таблица", combined)
        self.assertIn("https://github.com/owner/repo/actions/runs/1", combined)

    def test_job_failure_without_collector_output_is_visible(self):
        result = "\n".join(messages("", [], "https://github.com/run/1",
                                   {"failedGroups": [], "jobFailed": False},
                                   {"failedGroups": [], "jobFailed": True}))
        self.assertIn("Сбой задания", result)
        self.assertIn("https://github.com/run/1", result)

    def test_failed_delivery_fails_without_leaking_token(self):
        env = {"TELEGRAM_BOT_TOKEN": "secret-token", "TELEGRAM_CHAT_ID": "123",
               "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "owner/repo", "GITHUB_RUN_ID": "1"}
        output = io.StringIO()
        with patch.dict("os.environ", env), patch("products_alert.urlopen", side_effect=OSError("https://api.telegram.org/botsecret-token")), redirect_stderr(output):
            self.assertEqual(1, main())
        self.assertIn("Не удалось доставить", output.getvalue())
        self.assertNotIn("secret-token", output.getvalue())


if __name__ == "__main__":
    unittest.main()
