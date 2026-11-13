#!/usr/bin/env python3
"""Give each operation in an EF idempotent SQL Server script its own batch.

SQL Server compiles a batch before it runs. ``dotnet ef migrations script --idempotent``
keeps one migration in one batch, so a statement that uses a column the same migration
just added fails on an empty database with "Invalid column name". ``GO`` is a client
separator: the transaction stays open, and the next batch sees the column.

A line that is exactly ``END;`` closes one operation. The history table's ``END;`` is
already followed by ``GO``. Nested blocks in this script close with ``END`` and no
semicolon, so they stay in the operation's batch, which is what a ``DECLARE`` needs.
"""

from __future__ import annotations

import sys
from pathlib import Path

BOM = b"\xef\xbb\xbf"


def split_batches(sql: str) -> str:
    lines = sql.split("\n")
    out: list[str] = []
    for index, line in enumerate(lines):
        out.append(line)
        if line != "END;":
            continue
        follower = next((lines[j] for j in range(index + 1, len(lines)) if lines[j] != ""), None)
        if follower != "GO":
            out.append("GO")
    return "\n".join(out)


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: split_batches.py <migrations.sql>", file=sys.stderr)
        return 2
    path = Path(argv[1])
    raw = path.read_bytes()
    bom = BOM if raw.startswith(BOM) else b""
    text = raw[len(bom):].decode("utf-8")
    path.write_bytes(bom + split_batches(text).encode("utf-8"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
