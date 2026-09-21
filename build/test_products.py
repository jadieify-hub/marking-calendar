"""Offline checks for the supported actual ЧЗ layouts and safe publication."""
import json
import io
import re
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
    group = url.split("/projects/")[1].split("/")[0]
    if group == "chemistry":
        raise OSError("HTTP 404: source unavailable")
    if url.endswith("/faq/"):
        return (FIXTURES.parent / "product-scopes" / f"{group}.html").read_text(encoding="utf-8")
    return html(group)


class ProductTests(unittest.TestCase):
    def setUp(self):
        self.enterContext(redirect_stdout(io.StringIO()))
        self.enterContext(redirect_stderr(io.StringIO()))

    def test_all_available_source_formats_keep_names_codes_and_notes(self):
        for fixture in FIXTURES.glob("*.html"):
            with self.subTest(group=fixture.stem):
                parsed = products.parse_group(fixture.stem, html(fixture.stem), {})
                self.assertTrue(parsed["sourceHeading"])
                self.assertTrue(parsed["rows"])
                self.assertTrue(all(r["sourceName"] and (r["tnvedText"] or r["okpd2Text"]) for r in parsed["rows"]))
        beer = products.parse_group("beer", html("beer"), {})
        self.assertIn("пиво крепостью", beer["rows"][0]["sourceName"])
        devices = products.parse_group("medical_devices", html("medical_devices"), {})
        self.assertIn("131980", devices["rows"][0]["conditions"])
        water = products.parse_group("water", html("water"), {})
        self.assertEqual("2201", water["rows"][-1]["tnvedText"])
        self.assertEqual("10.86.10.310", water["rows"][-1]["okpd2Text"])
        self.assertIn("Лед, снег", water["conditions"])
        dairy = products.parse_group("dairy", html("dairy"), {})
        self.assertEqual(1, dairy["rows"][1]["sourceName"].casefold().count("мороженое и прочие"))
        self.assertIn("за исключением", dairy["rows"][1]["sourceName"])
        medicines = products.parse_group("medicines", html("medicines"), {})
        self.assertIn("3002150000", products.codes(medicines["rows"][0]["tnvedText"]))
        self.assertIn("не относится к ветеринарным", medicines["conditions"])
        beverages = products.parse_group("beverages", html("beverages"), {})
        self.assertEqual(4, len(beverages["rows"]))
        self.assertIn("ПЭТ", beverages["rows"][0]["section"])
        self.assertIn("без отдельных наименований", beverages["rows"][0]["conditions"])
        veterinary = products.parse_group("veterinary_products", html("veterinary_products"), {})
        self.assertEqual(17, len(veterinary["rows"]))
        self.assertTrue(all(r["sourceName"] == veterinary["rows"][0]["sourceName"] for r in veterinary["rows"]))
        wheelchairs = products.parse_group("wheelchairs", html("wheelchairs"), {})
        self.assertTrue(wheelchairs["rows"][0]["tnvedText"])
        self.assertTrue(wheelchairs["rows"][-1]["okpd2Text"])

    def test_code_lookup_preserves_real_spacing_and_rejects_long_numbers(self):
        self.assertEqual(["2202991100", "293629000", "1604310000"],
                         products.codes("2202 99110 0, 2936 29 000; 1604310000; 123456789012"))

    def test_product_scope_does_not_promote_classifier_labels_to_goods(self):
        sport = products.parse_group("sportpit", html("sportpit"), {})
        self.assertEqual([], sport["scope"]["names"])
        self.assertIn("СГР", sport["scope"]["description"])
        caviar = products.parse_group("caviar", html("caviar"), {})
        self.assertEqual(["Икра осетровых рыб", "Икра лососевых рыб."], caviar["scope"]["names"])
        self.assertTrue(any("молоки" in row["sourceName"] for row in caviar["rows"]))
        toys = products.parse_group("children", html("children"), {})
        self.assertIn("до 14 лет", toys["scope"]["names"][0])
        self.assertEqual(4, len(toys["scope"]["names"]))
        chairs = products.parse_group("wheelchairs", html("wheelchairs"), {})
        self.assertEqual(2, len(chairs["scope"]["names"]))
        pipes = products.parse_group("polymerpipes", html("polymerpipes"), {})
        self.assertTrue(all("\n" in name for name in pipes["scope"]["names"]))
        cigarettes = products.parse_group("electronic_cigarettes", html("electronic_cigarettes"), {})
        self.assertEqual(1, len(cigarettes["scope"]["names"]))
        self.assertNotIn("Смартфоны", cigarettes["scope"]["names"][0])
        for group in products.SCOPE_QUESTIONS:
            source = fetch(f"https://{products.CHZ_HOST}/business/projects/{group}/faq/")
            scope = products.parse_scope_faq(group, source)
            self.assertTrue(scope["description"])
            self.assertNotIn("воды", " ".join(scope["names"]).lower())
            with self.assertRaises(ValueError):
                products.parse_scope_faq(group, source.replace("qa-block__question", "missing"))

    def test_missing_card_codes_or_source_section_is_not_partial_success(self):
        for group, marker in (("water", "marking-retractable-block__content-code"),
                              ("beverages", "milk-marks-table__text")):
            with self.subTest(group=group), self.assertRaises(ValueError):
                products.parse_group(group, html(group).replace(marker, "broken"), {})
        # Removing a complete code field still leaves a plausible card with its other classifier.
        broken = html("medicines").replace("marking-retractable-block__content-row", "broken", 1)
        with self.assertRaises(ValueError):
            products.parse_group("medicines", broken, {})

    def test_missing_names_classifiers_and_critical_note_blocks_are_rejected(self):
        original = html("medical_devices")
        cells = list(re.finditer(r"<td\b[^>]*>.*?</td>", original, re.S))
        for index in (6, 7):
            broken = original[:cells[index].start()] + "<td></td>" + original[cells[index].end():]
            with self.subTest(column=index), self.assertRaises(ValueError):
                products.parse_group("medical_devices", broken, {})
        for group, tag, marker in (("caviar", "p", "Обращаем"), ("water", "ul", "Лед"),
                                    ("medicines", "h2", "ветеринарным"), ("dietarysup", "h1", "свидетельство")):
            original = html(group)
            block = next(block for block in re.findall(fr"<{tag}\b[^>]*>.*?</{tag}>", original, re.S) if marker in block)
            with self.subTest(group=group), self.assertRaises(ValueError):
                products.parse_group(group, original.replace(block, ""), {})

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

    def test_children_keeps_shared_okpd_and_exclusions_outside_table(self):
        group = products.parse_group("children", html("children"), {})
        self.assertEqual(4, len(group["rows"]))
        self.assertTrue(all(row["okpd2Text"] == "32.40" for row in group["rows"]))
        self.assertIn("до 14 лет", group["rows"][0]["sourceName"])
        self.assertIn("воздушных шаров", group["conditions"])
        self.assertIn("приложении 1", group["conditions"])
        with self.assertRaises(ValueError):
            products.parse_group("children", html("children").replace("воздушных шаров", ""), {})

    def test_excluded_code_is_a_glossary_entry_not_an_extra_product(self):
        meaning = {"code": "3307410000", "name": "Благовония", "searchTerms": [],
                   "sourceUrl": "https://eec.eaeunion.org/", "sourceContext": "Средства, сгорающие при использовании"}
        group = products.parse_group("cosmetics", html("cosmetics"), {"3307410000": meaning})
        self.assertEqual(5, len(group["rows"]))
        self.assertIn(meaning, group["rows"][1]["meanings"])
        self.assertIn("кроме", group["rows"][1]["tnvedText"])

    def test_failed_group_is_retained_while_another_group_updates(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(products, "fetch_page", side_effect=fetch):
            self.assertEqual(2, products.check(directory))  # Retired chemistry URL returns 404.
            path = Path(directory) / "products.json"
            old = json.loads(path.read_text(encoding="utf-8"))
            expected = {g["link"].strip("/").split("/")[-1]
                        for g in json.loads((products.ROOT / "assets/groups/groups.json").read_text(encoding="utf-8-sig"))["groups"]}
            self.assertEqual(expected - {"chemistry"}, {g["id"] for g in old["groups"]})
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

    def test_dry_run_writes_nothing_but_partial_first_fetch_publishes_valid_groups(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(products, "fetch_page", side_effect=fetch):
            self.assertEqual(2, products.check(directory, dry_run=True))
            self.assertEqual([], list(Path(directory).iterdir()))
            with patch.object(products, "fetch_page", side_effect=lambda url: "<html></html>" if "/homeware/" in url else fetch(url)):
                self.assertEqual(2, products.check(directory))
            path = Path(directory) / "products.json"
            first = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(48, len(first["groups"]))
            self.assertNotIn("homeware", {g["id"] for g in first["groups"]})
            self.assertEqual(2, products.check(directory))
            self.assertEqual(49, len(json.loads(path.read_text(encoding="utf-8"))["groups"]))

    def test_total_first_failure_does_not_create_empty_catalog(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(products, "fetch_page", side_effect=OSError("offline")):
            self.assertEqual(2, products.check(directory))
            self.assertFalse((Path(directory) / "products.json").exists())


if __name__ == "__main__":
    unittest.main()
