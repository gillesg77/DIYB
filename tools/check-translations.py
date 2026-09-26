"""Contrôle des fichiers de langue : JSON valide, mêmes clés que la référence
anglaise, et marqueurs de substitution identiques.

    python tools/check-translations.py
"""

import io
import json
import os
import re
import sys

STRINGS = os.path.join(os.path.dirname(__file__), "..", "src", "DIYB.Localization", "Strings")
REFERENCE = "en.json"
PLACEHOLDER = re.compile(r"\{[A-Za-z0-9_]+\}")


def load(path):
    with io.open(path, encoding="utf-8") as handle:
        return json.load(handle)


def main():
    reference = load(os.path.join(STRINGS, REFERENCE))
    failures = 0

    for name in sorted(os.listdir(STRINGS)):
        if not name.endswith(".json") or name == REFERENCE:
            continue

        path = os.path.join(STRINGS, name)
        try:
            catalog = load(path)
        except (ValueError, UnicodeDecodeError) as error:
            print("%-14s JSON illisible : %s" % (name, error))
            failures += 1
            continue

        problems = []

        missing = [k for k in reference if k not in catalog]
        if missing:
            problems.append("%d cle(s) manquante(s) : %s" % (len(missing), ", ".join(missing[:5])))

        extra = [k for k in catalog if k not in reference]
        if extra:
            problems.append("%d cle(s) en trop : %s" % (len(extra), ", ".join(extra[:5])))

        # Un marqueur oublie casse le formatage a l'execution.
        for key, value in catalog.items():
            if key not in reference:
                continue

            expected = set(PLACEHOLDER.findall(reference[key]))
            actual = set(PLACEHOLDER.findall(value))
            if expected != actual:
                problems.append("%s : marqueurs %s au lieu de %s" % (key, sorted(actual), sorted(expected)))

        if not catalog.get("language.name"):
            problems.append("language.name absent")

        if problems:
            failures += 1
            print("%-14s KO" % name)
            for problem in problems[:6]:
                print("               - %s" % problem)
        else:
            print("%-14s ok (%d cles)" % (name, len(catalog)))

    print("\n%d fichier(s) en defaut." % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
