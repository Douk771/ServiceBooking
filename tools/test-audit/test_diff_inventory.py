"""Unit-тесты логики сверки и разбора исходников (цикл 36). Запуск: python3 -m unittest discover -s tools/test-audit"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import diff_inventory as di  # noqa: E402
import inventory as inv  # noqa: E402


def test(cls, method, tid=None, cases=1, protected=False):
    return {"key": "%s.%s" % (cls, method), "id": tid, "className": cls, "method": method, "cases": cases,
            "protectedZone": protected}


def row(num, suite, tid, name, cat, action, repl="—"):
    return "| %s | %s | %s | %s | %s | %s | причина | %s |" % (num, suite, tid, name, cat, action, repl)


def snapshot(suite, tests):
    return {"suite": suite, "tests": tests, "totalCases": sum(t["cases"] for t in tests)}


def run_diff(suite, before, after, rows=()):
    with tempfile.TemporaryDirectory() as d:
        paths = {}
        for name, data in (("b", snapshot(suite, before)), ("a", snapshot(suite, after))):
            paths[name] = os.path.join(d, name + ".json")
            json.dump(data, open(paths[name], "w"))
        reg = os.path.join(d, "reg.md")
        open(reg, "w", encoding="utf-8").write("## Цикл 36 — ревизия\n\n" + "\n".join(rows) + "\n")
        proc = subprocess.run([sys.executable, os.path.join(HERE, "diff_inventory.py"), "--suite", suite,
                               "--before", paths["b"], "--after", paths["a"], "--registry", reg],
                              capture_output=True, text=True)
        return proc.returncode, proc.stdout


class DiffInventoryTests(unittest.TestCase):
    def test_same_snapshots_pass(self):
        t = [test("A", "m", "CY22-02")]
        self.assertEqual(run_diff("functional", t, t)[0], 0)

    def test_removing_one_of_duplicate_id_is_detected(self):
        before = [test("Cycle22ChannelFundingTests", m, "CY22-02", protected=True) for m in ("a", "b", "c")]
        code, out = run_diff("functional", before, before[:2])
        self.assertEqual(code, 4, out)
        self.assertIn("Cycle22ChannelFundingTests.c", out)

    def test_class_move_with_unique_pair_is_matched(self):
        before = [test("Old", "m", "CY1-01")]
        after = [test("New", "m", "CY1-01")]
        self.assertEqual(run_diff("functional", before, after)[0], 0)

    def test_method_rename_in_same_class_is_not_matched(self):
        before = [test("C", "old", "CY1-01")]
        after = [test("C", "new", "CY1-01")]
        self.assertEqual(run_diff("functional", before, after)[0], 4)

    def test_class_move_with_duplicate_pair_is_not_matched(self):
        before = [test("A", "m", "CY1-01"), test("B", "m", "CY1-01")]
        after = [test("A", "m", "CY1-01"), test("C", "m", "CY1-01")]
        self.assertEqual(run_diff("functional", before, after)[0], 4)

    def test_registry_row_by_id_does_not_cover_other_tests_with_same_id(self):
        before = [test("C", m, "CY9-01") for m in ("a", "b")]
        rows = [row("R36-B001", "functional", "CY9-01", "C.a", "А", "удалён")]
        code, out = run_diff("functional", before, [before[1]], rows)
        self.assertEqual(code, 0, out)
        code, out = run_diff("functional", before, [], rows)
        self.assertEqual(code, 4, out)

    def test_removed_protected_is_reported(self):
        before = [test("BillingTests", "m", "BLL-1", protected=True)]
        rows = [row("R36-B001", "functional", "BLL-1", "—", "А", "удалён")]
        code, out = run_diff("functional", before, [], rows)
        self.assertEqual(code, 4)
        self.assertIn("Удалён защищённый: 1", out)

    def test_cases_change_without_row_is_reported(self):
        self.assertEqual(run_diff("unit", [test("C", "m", cases=3)], [test("C", "m", cases=2)])[0], 4)

    def test_new_test_referenced_in_unit_replacement(self):
        before = [test("C", "gone")]
        rows = [row("R36-B001", "unit", "—", "C.gone", "Б", "удалён", "N.new; other")]
        self.assertEqual(run_diff("unit", before, [test("N", "new")], rows)[0], 0)

    def test_new_test_substring_in_replacement_is_not_enough(self):
        before = [test("C", "gone")]
        rows = [row("R36-B001", "unit", "—", "C.gone", "Б", "удалён", "N.newer")]
        self.assertEqual(run_diff("unit", before, [test("N", "new")], rows)[0], 4)

    def test_merged_from_develop_row_covers_new_and_changed_tests_of_class_by_prefix(self):
        rows = [row("R36-M001", "unit", "—", "N", "Е", "пришло мерджем develop")]
        before = [test("N", "old", cases=1)]
        after = [test("N", "old", cases=2), test("N", "new")]
        self.assertEqual(run_diff("unit", before, after, rows)[0], 0)

    def test_merged_from_develop_row_does_not_cover_other_class_with_same_prefix(self):
        rows = [row("R36-M001", "unit", "—", "N", "Е", "пришло мерджем develop")]
        self.assertEqual(run_diff("unit", [], [test("NN", "new")], rows)[0], 4)

    def test_merged_from_develop_row_does_not_hide_a_removed_test(self):
        rows = [row("R36-M001", "unit", "—", "N", "Е", "пришло мерджем develop")]
        self.assertEqual(run_diff("unit", [test("N", "gone")], [], rows)[0], 4)

    def test_merged_from_develop_row_covers_vitest_file(self):
        rows = [row("R36-M001", "vitest", "—", "src/a.test.ts", "Е", "пришло мерджем develop")]
        new = {"key": "src/a.test.ts::suite > it", "id": None, "className": "src/a.test.ts", "method": "it", "cases": 1,
               "protectedZone": False}
        self.assertEqual(run_diff("vitest", [], [new], rows)[0], 0)


class TestCaseIdTests(unittest.TestCase):
    def test_id_is_taken_from_own_class(self):
        idx = inv.SourceIndex.__new__(inv.SourceIndex)
        idx.files = {"f.cs": (
            "public class A\n{\n    [Fact]\n    [TestCase(\"X-1\")]\n    public void Same() { }\n}\n"
            "public class B\n{\n    [Fact]\n    public void Same() { }\n}\n")}
        self.assertEqual(idx.test_case_id("f.cs", "Same", "A"), "X-1")
        self.assertIsNone(idx.test_case_id("f.cs", "Same", "B"))


if __name__ == "__main__":
    unittest.main()
