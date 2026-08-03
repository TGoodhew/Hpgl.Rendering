// -----------------------------------------------------------------------------
// Golden-file plumbing shared by the regression suites.
//
// Two comparison forms, chosen deliberately:
//
//   SVG snapshots  - compared as EXACT normalised text. The SVG writer emits
//                    integer coordinates only (no decimals anywhere), so its
//                    output is bit-identical run to run and culture-proof. That
//                    makes an exact compare safe, and an exact compare is what
//                    turns "the arcs changed" into a reviewable line diff.
//
//   PNG goldens    - compared with a per-channel/per-fraction tolerance, because
//                    GDI+ text antialiasing genuinely differs between machines
//                    and runtimes. Used only where raster output IS the thing
//                    under test; everything expressible as SVG is asserted there
//                    instead, where the comparison is exact.
//
// Both forms share one property worth stating: a single golden file is compared
// by BOTH target frameworks. A render that differs between net472 and
// net8.0-windows therefore fails one leg or the other of the same test run, so
// cross-framework stability is enforced structurally rather than by a separate
// test that could drift out of step.
//
// Regeneration: set HPGL_REGEN_GOLDENS=1 and run the suite. Files are rewritten
// in the SOURCE tree (not the build output), so the change lands as a reviewable
// diff. Goldens must be REVIEWED, never blindly regenerated - a regenerated
// golden that nobody looked at asserts nothing at all.
//
//   $env:HPGL_REGEN_GOLDENS=1; dotnet test; $env:HPGL_REGEN_GOLDENS=$null
//   git diff tests/fixtures/golden   # <- read this before committing
//
// The HP-GL plotter-emulation technique is derived from the HP7470A Plotter
// Emulator (7470.cpp) by John Miles, KE5FX - http://www.ke5fx.com/
// -----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Hpgl.Rendering.Tests
{
    internal static class GoldenFile
    {
        /// <summary>True when the suite has been asked to rewrite goldens instead of asserting against them.</summary>
        public static bool Regenerating =>
            Environment.GetEnvironmentVariable("HPGL_REGEN_GOLDENS") == "1";

        /// <summary>Goldens as copied next to the test binary - the read path.</summary>
        private static string OutputDir => Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");

        /// <summary>
        /// Goldens in the repository - the write path for regeneration. Supplied by the
        /// project file as assembly metadata, because AppContext.BaseDirectory is somewhere
        /// under bin/ and cannot be walked back to the source tree reliably.
        /// </summary>
        private static string SourceDir
        {
            get
            {
                foreach (AssemblyMetadataAttribute a in typeof(GoldenFile).Assembly
                             .GetCustomAttributes<AssemblyMetadataAttribute>())
                    if (a.Key == "GoldenSourceDir")
                        return Path.GetFullPath(a.Value);

                throw new InvalidOperationException(
                    "GoldenSourceDir assembly metadata is missing - see Hpgl.Rendering.Tests.csproj. " +
                    "Without it goldens cannot be regenerated.");
            }
        }

        public static string FixturePath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "fixtures", name);

        /// <summary>
        /// Writes a golden only when its content actually changed, retrying briefly on IO
        /// contention. `dotnet test` runs both target frameworks concurrently and both legs
        /// regenerate into the same source directory - normally writing byte-identical
        /// content, so the second leg finds nothing to do and never opens the file. The
        /// retry covers the narrow window where they do collide.
        /// </summary>
        private static void WriteIfChanged(string file, byte[] content)
        {
            Directory.CreateDirectory(SourceDir);
            string path = Path.Combine(SourceDir, file);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path) && ContentEquals(File.ReadAllBytes(path), content)) return;
                    File.WriteAllBytes(path, content);
                    return;
                }
                catch (IOException) when (attempt < 10)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
        }

        private static bool ContentEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        // ---- SVG snapshots ---------------------------------------------------

        /// <summary>
        /// Compares <paramref name="svg"/> to the committed snapshot, or rewrites it when
        /// regenerating. The comparison is exact after normalisation.
        /// </summary>
        public static void AssertSvgMatches(string name, string svg)
        {
            string actual = NormaliseSvg(svg);
            string file = name + ".svg";

            if (Regenerating)
            {
                WriteIfChanged(file, new UTF8Encoding(false).GetBytes(actual));
                return;
            }

            string path = Path.Combine(OutputDir, file);
            Assert.True(File.Exists(path),
                "missing SVG golden '" + file + "'. Create it with HPGL_REGEN_GOLDENS=1 and review the result " +
                "before committing - an unreviewed golden asserts nothing.");

            string expected = Normalise(File.ReadAllText(path));
            if (expected == actual) return;

            Assert.Fail(
                "SVG snapshot '" + file + "' differs.\n" + FirstDifference(expected, actual) +
                "\nIf the renderer changed intentionally: HPGL_REGEN_GOLDENS=1 dotnet test, then READ the diff.");
        }

        /// <summary>
        /// Splits the document one element per line, and each path's <c>d</c> attribute one
        /// subpath per line. Purely cosmetic - applied to both sides of the comparison - but
        /// it is what makes "one arc moved" show up as one changed line instead of a single
        /// 8 KB line that no reviewer can read.
        /// </summary>
        private static string NormaliseSvg(string svg)
        {
            string s = Regex.Replace(svg, @"(?<!^)<", "\n<");
            s = Regex.Replace(s, @"\sd=""([^""]*)""", m =>
                " d=\"" + m.Groups[1].Value.Replace("M", "\n    M").TrimStart() + "\"");
            return Normalise(s);
        }

        private static string Normalise(string s) =>
            s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd() + "\n";

        private static string FirstDifference(string expected, string actual)
        {
            string[] e = expected.Split('\n'), a = actual.Split('\n');
            for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
            {
                string le = i < e.Length ? e[i] : "<end of file>";
                string la = i < a.Length ? a[i] : "<end of file>";
                if (le != la)
                    return "  first difference at line " + (i + 1) +
                           "\n    expected: " + Truncate(le) +
                           "\n    actual:   " + Truncate(la) +
                           "\n  (" + e.Length + " expected lines, " + a.Length + " actual)";
            }
            return "  (files differ only in trailing content)";
        }

        private static string Truncate(string s) =>
            s.Length <= 160 ? s : s.Substring(0, 160) + "... (+" + (s.Length - 160) + " chars)";

        // ---- PNG goldens -----------------------------------------------------

        /// <summary>
        /// Compares a rendered bitmap to its golden. A pixel counts as differing when any
        /// channel is off by more than <paramref name="channelDelta"/>; the test fails when
        /// more than <paramref name="maxDiffFraction"/> of pixels differ.
        ///
        /// The tolerance exists for exactly one reason - GDI+ glyph antialiasing is not
        /// identical across machines and runtimes - and is deliberately tight enough that a
        /// structural break (clipped output, a dropped fill, a wrong transform) moves far
        /// more pixels than it allows.
        /// </summary>
        public static void AssertPngMatches(
            string name, byte[] png, int channelDelta = 32, double maxDiffFraction = 0.02)
        {
            string file = name + ".png";

            if (Regenerating)
            {
                WriteIfChanged(file, png);
                return;
            }

            string path = Path.Combine(OutputDir, file);
            Assert.True(File.Exists(path),
                "missing PNG golden '" + file + "'. Create it with HPGL_REGEN_GOLDENS=1 and LOOK at the image " +
                "before committing.");

            using (var ms = new MemoryStream(png))
            using (var rendered = new Bitmap(ms))
            using (var golden = new Bitmap(path))
            {
                Assert.True(golden.Width == rendered.Width && golden.Height == rendered.Height,
                    "golden '" + file + "' is " + golden.Width + "x" + golden.Height +
                    ", render is " + rendered.Width + "x" + rendered.Height);

                long differing = 0;
                long total = (long)rendered.Width * rendered.Height;
                for (int y = 0; y < rendered.Height; y++)
                    for (int x = 0; x < rendered.Width; x++)
                    {
                        Color r = rendered.GetPixel(x, y), g = golden.GetPixel(x, y);
                        if (Math.Abs(r.R - g.R) > channelDelta ||
                            Math.Abs(r.G - g.G) > channelDelta ||
                            Math.Abs(r.B - g.B) > channelDelta)
                            differing++;
                    }

                double fraction = (double)differing / total;
                Assert.True(fraction < maxDiffFraction, string.Format(
                    CultureInfo.InvariantCulture,
                    "PNG golden '{0}' differs in {1:P3} of pixels (allowed < {2:P1}); " +
                    "{3}/{4} px beyond ±{5}/channel. If the renderer changed intentionally, " +
                    "regenerate with HPGL_REGEN_GOLDENS=1 and LOOK at the image before committing.",
                    file, fraction, maxDiffFraction, differing, total, channelDelta));
            }
        }
    }
}
