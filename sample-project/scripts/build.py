"""Tiny build helper for the synthetic Inventory API fixture.

Used by aigest smoke tests to verify that the corpus loader handles
non-C# files correctly. Not a real build script.
"""

import os
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = PROJECT_ROOT / "src"
TEST_DIR = PROJECT_ROOT / "tests"


def count_lines(directory: Path, suffix: str) -> int:
    if not directory.exists():
        return 0
    total = 0
    for path in directory.rglob(f"*{suffix}"):
        with path.open() as fh:
            total += sum(1 for _ in fh)
    return total


def main() -> int:
    src_lines = count_lines(SRC_DIR, ".cs")
    test_lines = count_lines(TEST_DIR, ".cs")
    print(f"src/ {src_lines} lines, tests/ {test_lines} lines")
    return 0


if __name__ == "__main__":
    sys.exit(main())
