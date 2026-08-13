#!/usr/bin/env python
"""
build-min.py - produce a comment-stripped SkippyShuttle.min.cs for pasting into
the Programmable Block.

Space Engineers caps a PB script at 100,000 source characters, counting comments
and whitespace. SkippyShuttle.cs is kept fully commented as the source of truth;
this tool emits the deploy artifact the game actually runs, well under the cap.

What it strips (character-state aware, so it never touches a "//" or "/*" that
lives inside a string or char literal):
  - // line comments
  - /* ... */ block comments
  - blank / whitespace-only lines
  - trailing whitespace on every line

What it preserves: all code, string/char literals verbatim, and line indentation
(so the min file stays glanceable). Line numbers do NOT match the source once
comments and blank lines are removed - debug against SkippyShuttle.cs, not the min.

Usage (from anywhere):
    python tools/build-min.py
Writes SkippyShuttle.min.cs next to SkippyShuttle.cs and prints a size report.
Exits non-zero if the output still exceeds the limit or braces don't balance.
"""

import os
import sys

LIMIT = 100_000

# Character-state machine states.
CODE, LINE_COMMENT, BLOCK_COMMENT, STRING, VERBATIM, CHAR = range(6)


def strip_comments(src):
    """Return src with C# comments removed, literals left intact."""
    out = []
    i = 0
    n = len(src)
    state = CODE
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""

        if state == CODE:
            if c == "/" and nxt == "/":
                state = LINE_COMMENT
                i += 2
                continue
            if c == "/" and nxt == "*":
                state = BLOCK_COMMENT
                i += 2
                continue
            if c == "@" and nxt == '"':
                out.append(c)
                out.append(nxt)
                state = VERBATIM
                i += 2
                continue
            if c == '"':
                out.append(c)
                state = STRING
                i += 1
                continue
            if c == "'":
                out.append(c)
                state = CHAR
                i += 1
                continue
            out.append(c)
            i += 1
            continue

        if state == LINE_COMMENT:
            # Keep the newline so line structure survives.
            if c == "\n":
                out.append(c)
                state = CODE
            i += 1
            continue

        if state == BLOCK_COMMENT:
            if c == "*" and nxt == "/":
                state = CODE
                i += 2
                continue
            # Preserve newlines inside block comments so lines don't merge.
            if c == "\n":
                out.append(c)
            i += 1
            continue

        if state == STRING:
            out.append(c)
            if c == "\\":  # escape: copy the next char verbatim
                if i + 1 < n:
                    out.append(nxt)
                    i += 2
                    continue
            elif c == '"':
                state = CODE
            i += 1
            continue

        if state == VERBATIM:
            # In a verbatim string, "" is an escaped quote; \ is literal.
            if c == '"' and nxt == '"':
                out.append(c)
                out.append(nxt)
                i += 2
                continue
            out.append(c)
            if c == '"':
                state = CODE
            i += 1
            continue

        if state == CHAR:
            out.append(c)
            if c == "\\":
                if i + 1 < n:
                    out.append(nxt)
                    i += 2
                    continue
            elif c == "'":
                state = CODE
            i += 1
            continue

    return "".join(out)


def drop_blank_lines(src):
    """Trim trailing whitespace and drop whitespace-only lines."""
    kept = [ln.rstrip() for ln in src.splitlines()]
    kept = [ln for ln in kept if ln.strip() != ""]
    return "\n".join(kept) + "\n"


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)
    src_path = os.path.join(root, "SkippyShuttle.cs")
    out_path = os.path.join(root, "SkippyShuttle.min.cs")

    with open(src_path, "r", encoding="utf-8") as f:
        src = f.read()

    stripped = drop_blank_lines(strip_comments(src))

    before = len(src)
    after = len(stripped)
    opens = stripped.count("{")
    closes = stripped.count("}")

    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(stripped)

    saved = before - after
    pct = (saved / before * 100) if before else 0
    print(f"source : {before:>7,} chars  ({os.path.basename(src_path)})")
    print(f"min    : {after:>7,} chars  ({os.path.basename(out_path)})")
    print(f"saved  : {saved:>7,} chars  ({pct:.1f}%)")
    print(f"headroom under {LIMIT:,}: {LIMIT - after:,} chars")
    print(f"braces : {{ {opens}  }} {closes}  {'OK' if opens == closes else 'MISMATCH'}")

    problems = []
    if opens != closes:
        problems.append("brace mismatch in output")
    if after > LIMIT:
        problems.append(f"output exceeds {LIMIT:,}-char limit")
    if problems:
        print("FAIL: " + "; ".join(problems), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
