"""Offline checks for the three actual ЧЗ layouts and safe publication."""
import json
import io
from contextlib import redirect_stdout, redirect_stderr
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import products

FIXTURES = Path(__file__).resolve().parents[1] / "tests/fixtures/products"


def html(group):
    return (FIXTURES / f"{group}.html").read_text(encoding="utf-8")


def fetch(url):
    return html(url.split("/projects/")[1].split("/")[0])


class ProductTests(unittest.TestCase):
    def setUp(self):
        self.enterContext(redirect_stdout(io.StringIO()))
        self.enterContext(redirect_stderr(io.StringIO()))

    def test_actual_pages_keep_codes_stages_and_exceptions(self):
        home = products.parse_group("homeware", html("homeware"), {})
        grocery = products.parse_group("grocery", html("grocery"), {})
        cosmetics = products.parse_group("cosmetics", html("cosmetics"), {})
        self.assertEqual(3, len(home["rows"]))
        self.assertEqual("", home["rows"][0]["okpd2Text"])
        self.assertEqual(2, len(grocery["rows"]))
        self.assertIn("0712 20 000 0", grocery["rows"][1]["tnvedText"])
        self.assertIn("за исключением", grocery["rows"][1]["tnvedText"])
        self.assertIn("30 граммов", grocery["conditions"])
        self.assertEqual(5, len(cosmetics["rows"]))
        self.assertIn("1 октября 2026", cosmetics["rows"][-1]["section"])
        self.assertIn("регистрационное удостоверение", cosmetics["conditions"])
        self.assertIn("зубные ёршики", " ".join(cosmetics["examples"]))
        self.assertIn("кроме", cosmetics["rows"][1]["tnvedText"])

    def test_shell_and_missing_notes_cannot_replace_real_content(self):
        with self.assertRaises(ValueError):
            products.parse_group("homeware", "<html>Товары для дома</html>", {})
        broken = html("cosmetics").replace("milk-marks-table__text", "missing-notes")
        with self.assertRaises(ValueError):
            products.parse_group("cosmetics", broken, {})
        broken = html("cosmetics").replace("одновременно", "")
        with self.assertRaises(ValueError):
            products.parse_group("cosmetics", broken, {})
        with self.assertRaises(ValueError):
            products.parse_group("grocery", html("grocery").replace("—", "", 1), {})

    def test_excluded_code_is_a_glossary_entry_not_an_extra_product(self):
        meaning = {"code": "3307410000", "name": "Благовония", "searchTerms": [],
                   "sourceUrl": "https://eec.eaeunion.org/", "sourceContext": "Средства, сгорающие при использовании"}
        group = products.parse_group("cosmetics", html("cosmetics"), {"3307410000": meaning})
        self.assertEqual(5, len(group["rows"]))
        self.assertIn(meaning, group["rows"][1]["meanings"])
        self.assertIn("кроме", group["rows"][1]["tnvedText"])

    def test_failed_group_is_retained_while_another_group_updates(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(products, "fetch_page", side_effect=fetch):
            self.assertEqual(0, products.check(directory))
            path = Path(directory) / "products.json"
            old = json.loads(path.read_text(encoding="utf-8"))
            def changed(url):
                if "/cosmetics/" in url:
                    raise OSError("source unavailable")
                return fetch(url).replace("Декор и предметы интерьера", "Предметы декора и интерьера")
            with patch.object(products, "fetch_page", side_effect=changed):
                self.assertEqual(2, products.check(directory))
            new = json.loads(path.read_text(encoding="utf-8"))
            before = {g["id"]: g for g in old["groups"]}
            after = {g["id"]: g for g in new["groups"]}
            self.assertEqual(before["cosmetics"], after["cosmetics"])
            self.assertNotEqual(before["homeware"]["revision"], after["homeware"]["revision"])

    def test_verification_time_does_not_change_content_revision_but_notes_do(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(products, "fetch_page", side_effect=fetch):
            products.check(directory)
            path = Path(directory) / "products.json"
            old = json.loads(path.read_text(encoding="utf-8"))
            products.check(directory)
            repeated = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(old["revision"], repeated["revision"])
            self.assertEqual(old["groups"][0]["changedAt"], repeated["groups"][0]["changedAt"])
            with patch.object(products, "fetch_page", side_effect=lambda url: fetch(url).replace("30\u00a0граммов", "35\u00a0граммов")):
                products.check(directory)
            changed = json.loads(path.read_text(encoding="utf-8"))
            self.assertNotEqual(old["revision"], changed["revision"])

    def test_dry_run_and_incomplete_first_fetch_do_not_publish(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(products, "fetch_page", side_effect=fetch):
            self.assertEqual(0, products.check(directory, dry_run=True))
            self.assertEqual([], list(Path(directory).iterdir()))
            with patch.object(products, "fetch_page", side_effect=lambda url: "<html></html>" if "/homeware/" in url else fetch(url)):
                self.assertEqual(2, products.check(directory))
            self.assertFalse((Path(directory) / "products.json").exists())


if __name__ == "__main__":
    unittest.main()
