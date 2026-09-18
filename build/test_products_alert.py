"""Failure notifications identify affected groups without sending messages in tests."""
import io
from contextlib import redirect_stderr
import unittest
from unittest.mock import patch

from products_alert import main, messages


class AlertTests(unittest.TestCase):
    def test_names_reasons_and_run_link_survive_message_splitting(self):
        groups = [{"name": f"Группа {i}", "link": f"/business/projects/g{i}/"} for i in range(50)]
        log = "\n".join(f"g{i}: Не найдена таблица " + "x" * 100 for i in range(50))
        log += "\nPRODUCTS_FAILED=" + ",".join(f"g{i}" for i in range(50))
        result = messages(log, groups, "https://github.com/owner/repo/actions/runs/1")
        self.assertTrue(all(len(message) <= 3500 for message in result))
        combined = "\n".join(result)
        for i in range(50):
            self.assertIn(f"Группа {i}:", combined)
        self.assertIn("Не найдена таблица", combined)
        self.assertIn("https://github.com/owner/repo/actions/runs/1", combined)

    def test_job_failure_without_collector_output_is_visible(self):
        result = "\n".join(messages("", [], "https://github.com/run/1"))
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
