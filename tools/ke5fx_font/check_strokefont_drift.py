#!/usr/bin/env python3
"""
Drift guard for the generated src/Hpgl.Rendering/StrokeFont.cs scaffolding.

generate_strokefont.py needs the KE5FX renderer.cpp - a proprietary file that is not in
this repo and is not on CI runners - so a full "regenerate and diff the whole file"
cannot run in CI. But the divergence that motivated this guard (#37) lived entirely in
the generator's HEADER/FOOTER template, the hand-maintained C# scaffolding around the
glyph table, not in the glyph data that comes from renderer.cpp. The `Get(char, int)`
overload and `IsImplemented` had been hand-edited into the .cs while the generator still
emitted the one-argument `Get`, so a regenerate would have silently reverted them.

This check verifies that the committed StrokeFont.cs still begins with the generator's
HEADER and ends with its FOOTER, so the scaffolding cannot drift again without a build
also failing here. It does not - and cannot, without renderer.cpp - verify the glyph
table in between. Newline style is ignored: the file is committed CRLF, the template
constants are LF.

Exit 0 if they agree, 1 (with a message naming what diverged) otherwise.
"""
import sys

import generate_strokefont as gen  # import does not run main(): it is __main__-guarded


def _normalize_newlines(text):
    return text.replace("\r\n", "\n").replace("\r", "\n")


def main():
    with open(gen.OUT, encoding="utf-8") as handle:
        actual = _normalize_newlines(handle.read())
    header = _normalize_newlines(gen.HEADER)
    footer = _normalize_newlines(gen.FOOTER)

    problems = []
    if not actual.startswith(header):
        problems.append("StrokeFont.cs no longer begins with the generator's HEADER")
    if not actual.endswith(footer):
        problems.append("StrokeFont.cs no longer ends with the generator's FOOTER")

    if not problems:
        print(f"OK: {gen.OUT} scaffolding matches generate_strokefont.py")
        return 0

    for problem in problems:
        print(f"DRIFT: {problem}")
    print(
        "The committed file and generate_strokefont.py have diverged on the "
        "hand-maintained scaffolding (for example the Get / IsImplemented signatures). "
        "Update the generator's HEADER/FOOTER to match the file, then regenerate so the "
        "two agree - see tools/ke5fx_font/generate_strokefont.py."
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
