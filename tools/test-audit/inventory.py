#!/usr/bin/env python3
"""Инвентарь тестов одного набора (цикл 36, BE-36-02).

Контракт: API_CONTRACT_CYCLE36.md §36.26, форма: contracts/cycle36/test-inventory.schema.json.
Только стандартная библиотека. Коды выхода: 0 успех, 1 аргументы, 2 среда не готова.

    inventory.py --suite {unit|functional|vitest} --out <file.json>

unit/functional ждут уже собранные проекты (`dotnet build` до запуска), vitest - установленный node_modules.
"""
import argparse
import collections
import datetime
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJECTS = {"unit": "ServiceBooking.UnitTests", "functional": "ServiceBooking.Tests"}

# §36.9.3: защита по файлу (имя класса верхнего уровня) и по префиксу ID.
PROTECTED_CLASSES = {
    "LegalConsentTests", "LegalConsentVersionChangeTests", "LegalPriorityTests", "LegalPricingGateTests",
    "DataRightsTests", "Cycle20SubjectRequestsTests", "Cycle20HealthWrittenConsentTests",
    "Cycle20PlatformNoticesTests", "GuestDataGateCycle16Tests", "Cycle24PersonalDataTests", "ClientNotePhotosTests",
    "BillingTests", "PricingTests", "Cycle15PlansTests", "Cycle18TrialPlanTests", "Cycle18TrialLifecycleTests",
    "Cycle19TariffLimitsTests", "Cycle19RetiredLimitGateParityTests", "Cycle24TariffTests", "Cycle28TariffsTests",
    "Cycle22ChannelFundingTests", "AdminBillingAccountsTests", "Cycle20ManualPlanReasonTests",
    "AuthTests", "IdentityRoleSyncTests", "RateLimitingTests", "Cycle25RateLimitTests", "Cycle31GalleryRateLimitTests",
    "PushAddressGuardTests", "UploadsStaticFilesTests", "CompanyTransferTests", "Cycle20CompanyTransferLg6Tests",
    "Cycle23StrictModeAndDataTests", "Cycle28OutboundSuppressionTests", "Cycle28ShowcaseGuardsTests",
    "PhoneVerificationTests",
}
PROTECTED_ID_PREFIXES = ("LEG-", "LGL-", "SEC-", "PRC-", "BLL-", "ABA-", "CY18-", "CY18L-", "CY20-")
PROTECTED_UNIT_DIRS = ("/Services/Legal/", "/Services/Retention/", "/Services/Billing/", "/Services/Subjects/")
PROTECTED_FRONT_PREFIXES = (
    "src/components/legal/", "src/pages/ConsentsPage", "src/pages/SubjectRequestPage", "src/pages/LegalDocumentPage",
    "src/components/billing/", "src/components/pricing/", "src/pages/BillingPage", "src/pages/PricingPage",
    "src/utils/legal", "src/utils/healthConsent",
)
PROTECTED_FRONT_CONTAINS = (".captcha.test.", "CartPanel.test.")
ID_RE = re.compile(r"^[A-Z][A-Z0-9]*(-[A-Za-z0-9]+)+")


def run(cmd, cwd=ROOT, env_extra=None):
    env = dict(os.environ, **(env_extra or {}))
    return subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True)


def git_info():
    commit = run(["git", "rev-parse", "--short=12", "HEAD"]).stdout.strip()
    dirty = bool(run(["git", "status", "--porcelain", "--untracked-files=no"]).stdout.strip())
    return commit, dirty


def load_areas():
    with open(os.path.join(ROOT, "contracts", "cycle36", "test-areas.json"), encoding="utf-8") as f:
        return json.load(f)


# ---------- .NET ----------

ATTR_BLOCK_METHOD_RE = re.compile(
    r"((?:[ \t]*\[[^\n]*\][ \t]*\r?\n)+)[ \t]*public\s+(?:async\s+)?(?:static\s+)?[\w<>\[\],.?() ]+?\s+(\w+)\s*\(")
CLASS_RE = re.compile(
    r"((?:[ \t]*\[[^\n]*\][ \t]*\r?\n)*)[ \t]*(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*"
    r"class\s+(\w+)")
TESTCASE_RE = re.compile(r'TestCase\(\s*"([^"]+)"')
AREA_RE = re.compile(r'Trait\(\s*(?:"Area"|TestAreas\.TraitName)\s*,\s*(?:"([a-z]+)"|TestAreas\.(\w+))')


class SourceIndex:
    """Разбор исходников проекта: где объявлен класс, какие у него трейты Area, какой TestCase у метода."""

    def __init__(self, project):
        self.files = {}
        base = os.path.join(ROOT, project)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
            for name in filenames:
                if name.endswith(".cs"):
                    path = os.path.join(dirpath, name)
                    with open(path, encoding="utf-8-sig") as f:
                        self.files[os.path.relpath(path, ROOT)] = f.read()
        self.class_files = collections.defaultdict(list)
        for rel, text in self.files.items():
            for m in CLASS_RE.finditer(text):
                self.class_files[m.group(2)].append(rel)

    def locate(self, class_name, method):
        simple = class_name.split("+")[-1].split(".")[-1]
        candidates = self.class_files.get(simple, [])
        for rel in candidates:
            if re.search(r"\b" + re.escape(method) + r"\s*\(", self.files[rel]):
                return rel
        return candidates[0] if candidates else ""

    def test_case_id(self, rel, method):
        text = self.files.get(rel, "")
        for m in ATTR_BLOCK_METHOD_RE.finditer(text):
            if m.group(2) == method:
                found = TESTCASE_RE.search(m.group(1))
                if found:
                    return found.group(1)
        return None

    def areas(self, class_name):
        simple = class_name.split("+")[-1].split(".")[-1]
        result = set()
        for rel in self.class_files.get(simple, []):
            for m in CLASS_RE.finditer(self.files[rel]):
                if m.group(2) != simple:
                    continue
                for a in AREA_RE.finditer(m.group(1)):
                    result.add(a.group(1) or a.group(2).lower())
        return sorted(result)


def list_dotnet(project):
    proc = run(["dotnet", "test", project, "--no-build", "--list-tests"], env_extra={"DOTNET_CLI_UI_LANGUAGE": "en"})
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout[-2000:] + proc.stderr[-2000:])
        sys.stderr.write("Проект не собран? Выполните `dotnet build %s` до запуска.\n" % project)
        sys.exit(2)
    lines, started = [], False
    for line in proc.stdout.splitlines():
        if line.startswith("The following Tests are available"):
            started = True
            continue
        if started and line.startswith("    "):
            lines.append(line.strip())
    return lines


def is_dotnet_protected(class_name, rel, test_id, suite):
    top = class_name.split("+")[0].split(".")[-1]
    if top in PROTECTED_CLASSES:
        return True
    if test_id and test_id.startswith(PROTECTED_ID_PREFIXES):
        return True
    if suite == "unit" and any(d in "/" + rel.replace(os.sep, "/") for d in PROTECTED_UNIT_DIRS):
        return True
    return False


def build_dotnet(suite):
    project = PROJECTS[suite]
    index = SourceIndex(project)
    counts = collections.Counter()
    for line in list_dotnet(project):
        counts[line.split("(", 1)[0]] += 1
    tests = []
    for key in sorted(counts):
        class_name, _, method = key.rpartition(".")
        rel = index.locate(class_name, method)
        test_id = index.test_case_id(rel, method)
        entry = {
            "key": key,
            "id": test_id,
            "file": rel.replace(os.sep, "/"),
            "className": class_name,
            "method": method,
            "cases": counts[key],
            "areas": index.areas(class_name) if suite == "functional" else [],
            "protectedZone": is_dotnet_protected(class_name, rel, test_id, suite),
        }
        tests.append(entry)
    return tests


# ---------- vitest ----------

def build_vitest():
    frontend = os.path.join(ROOT, "frontend")
    if not os.path.isdir(os.path.join(frontend, "node_modules")):
        sys.stderr.write("Нет frontend/node_modules: выполните `npm ci`.\n")
        sys.exit(2)
    proc = run(["npx", "vitest", "list", "--json"], cwd=frontend)
    if proc.returncode != 0:
        sys.stderr.write(proc.stderr[-2000:])
        sys.exit(2)
    raw = json.loads(proc.stdout)
    areas = load_areas()["areas"]
    counts = collections.Counter()
    meta = {}
    for item in raw:
        rel = os.path.relpath(item["file"], frontend).replace(os.sep, "/")
        key = "%s::%s" % (rel, item["name"])
        counts[key] += 1
        meta[key] = (rel, item["name"])
    tests = []
    for key in sorted(counts):
        rel, name = meta[key]
        leaf = name.split(" > ")[-1]
        m = ID_RE.match(leaf)
        protected = rel.startswith(PROTECTED_FRONT_PREFIXES) or any(s in rel for s in PROTECTED_FRONT_CONTAINS)
        tests.append({
            "key": key,
            "id": m.group(0) if m else None,
            "file": "frontend/" + rel,
            "cases": counts[key],
            "areas": sorted(a["id"] for a in areas if any(rel.startswith(p) for p in a["frontendPathPrefixes"])),
            "protectedZone": protected,
        })
    return tests


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--suite", required=True, choices=["unit", "functional", "vitest"])
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    tests = build_vitest() if args.suite == "vitest" else build_dotnet(args.suite)
    commit, dirty = git_info()
    doc = {
        "schemaVersion": 1,
        "suite": args.suite,
        "commit": commit,
        "dirty": dirty,
        "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "totalCases": sum(t["cases"] for t in tests),
        "tests": tests,
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print("%s: %d тестов, %d запусков -> %s" % (args.suite, len(tests), doc["totalCases"], args.out))


if __name__ == "__main__":
    main()
