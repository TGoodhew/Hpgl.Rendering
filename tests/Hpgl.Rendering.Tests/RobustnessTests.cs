// -----------------------------------------------------------------------------
// Robustness of the public entry points against malformed, truncated and hostile
// input.
//
// The threat model is not hypothetical: input is bytes captured off a bus that is
// known to drop them - the repair passes exist precisely because captures arrive
// corrupt - and any service that renders a user-supplied plot inherits whatever
// this library does with bad bytes.
//
// THE CONTRACT ENFORCED HERE
//
//   For ANY byte sequence, a public entry point must either return, or throw
//   ArgumentException (or a subclass). Anything else is a failure.
//
// ArgumentException is permitted because it is the documented way to say "this
// input is not usable". The banned types are the ones that mean the parser lost
// track of its own state: IndexOutOfRangeException, NullReferenceException,
// OverflowException, InvalidCastException, KeyNotFoundException,
// ArithmeticException, FormatException. Those are defects, not input rejection.
//
// Note this suite asserts the contract; it does not define which arguments SHOULD
// be rejected in the first place - that is argument-validation work (#13).
//
// The HP-GL plotter-emulation technique is derived from the HP7470A Plotter
// Emulator (7470.cpp) by John Miles, KE5FX - http://www.ke5fx.com/
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Hpgl.Rendering;
using Xunit;

namespace Hpgl.Rendering.Tests
{
    public class RobustnessTests
    {
        /// <summary>
        /// Per-render wall-clock ceiling. Deliberately generous - it exists to catch a
        /// non-terminating loop or runaway subdivision, not to police performance, and it
        /// has to hold on a loaded shared CI runner. A pathological input that renders in
        /// 50 ms locally will not approach this; one that spins will blow straight past it.
        /// </summary>
        private static readonly TimeSpan RenderCeiling = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Approximate managed-heap ceiling for a single render. Measured with
        /// GC.GetTotalMemory, which is an approximation rather than a precise allocation
        /// count - GC.GetAllocatedBytesForCurrentThread does not exist on net472, so a
        /// portable exact measure is not available. Sized to catch "allocates without
        /// bound", not to pin steady-state usage.
        /// </summary>
        private const long HeapCeilingBytes = 512L * 1024 * 1024;

        private static readonly Type[] BannedExceptionTypes =
        {
            typeof(IndexOutOfRangeException),
            typeof(NullReferenceException),
            typeof(OverflowException),
            typeof(InvalidCastException),
            typeof(KeyNotFoundException),
            typeof(ArithmeticException),
            typeof(FormatException),
        };

        private static byte[] Latin1(string s) => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

        /// <summary>
        /// Runs one render, enforcing the exception contract and the work ceilings.
        /// Returns a description of what happened, for use in failure messages.
        /// </summary>
        private static string RenderMustBeWellBehaved(string what, Action render)
        {
            long before = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            string outcome;
            try
            {
                render();
                outcome = "returned";
            }
            catch (ArgumentException ex)
            {
                // Permitted: the documented way to reject unusable input.
                outcome = "threw " + ex.GetType().Name;
            }
            catch (Exception ex)
            {
                foreach (Type banned in BannedExceptionTypes)
                    if (banned.IsInstanceOfType(ex))
                        Assert.Fail(what + " threw " + ex.GetType().FullName +
                                    " - a parser that lost track of its own state, not input rejection.\n" +
                                    "Message: " + ex.Message + "\n" + ex.StackTrace);

                Assert.Fail(what + " threw undocumented " + ex.GetType().FullName +
                            ". Only ArgumentException (or a subclass) may escape a public entry point.\n" +
                            "Message: " + ex.Message);
                throw; // unreachable; keeps the compiler happy about definite assignment
            }
            sw.Stop();

            Assert.True(sw.Elapsed < RenderCeiling,
                what + " took " + sw.Elapsed + ", over the " + RenderCeiling + " ceiling - " +
                "suspect a non-terminating loop or unbounded subdivision.");

            long grew = GC.GetTotalMemory(false) - before;
            Assert.True(grew < HeapCeilingBytes,
                what + " grew the managed heap by ~" + (grew / (1024 * 1024)) + " MB, over the " +
                (HeapCeilingBytes / (1024 * 1024)) + " MB ceiling.");

            return outcome;
        }

        private static void RenderAllEntryPoints(string what, byte[] data)
        {
            RenderMustBeWellBehaved(what + " / RenderToPng", () => HpglRenderer.RenderToPng(data));
            RenderMustBeWellBehaved(what + " / RenderToSvg", () => HpglRenderer.RenderToSvg(data));
            RenderMustBeWellBehaved(what + " / RenderToBitmap", () =>
            {
                using (var bmp = HpglRenderer.RenderToBitmap(Encoding.GetEncoding("ISO-8859-1").GetString(data))) { }
            });
            RenderMustBeWellBehaved(what + " / HpglTraceRepair", () =>
                HpglTraceRepair.Repair(Encoding.GetEncoding("ISO-8859-1").GetString(data)));
            RenderMustBeWellBehaved(what + " / PclRasterRepair", () => PclRasterRepair.Repair(data));
            RenderMustBeWellBehaved(what + " / LooksLikePcl", () => PclRenderer.LooksLikePcl(data));

            if (PclRenderer.LooksLikePcl(data))
            {
                RenderMustBeWellBehaved(what + " / Pcl.RenderToPng", () => PclRenderer.RenderToPng(data));
                RenderMustBeWellBehaved(what + " / Pcl.RenderToSvg", () => PclRenderer.RenderToSvg(data));
            }
        }

        // ---- Truncation ------------------------------------------------------

        [Fact]
        public void Truncation_FeatureExercise_AtEveryOffset_IsWellBehaved()
        {
            // Exhaustive: every prefix length of the smallest real fixture. A capture cut
            // mid-instruction, mid-parameter or mid-label is the single most common way a
            // GPIB capture arrives broken, so at least one fixture gets full coverage.
            byte[] full = File.ReadAllBytes(GoldenFile.FixturePath("feature-exercise.plt"));
            for (int len = 0; len <= full.Length; len++)
            {
                var slice = new byte[len];
                Array.Copy(full, slice, len);
                RenderMustBeWellBehaved("truncated feature-exercise.plt at " + len,
                    () => HpglRenderer.RenderToPng(slice));
            }
        }

        [Theory]
        [InlineData("test.plt", 61)]
        [InlineData("test-print.pcl", 53)]
        public void Truncation_LargeFixtures_AtStridedOffsets_IsWellBehaved(string fixture, int stride)
        {
            // NOT every offset, deliberately, and stated rather than hidden: these fixtures
            // are 8 KB and 29 KB, and exhaustive sweeps of both on two target frameworks
            // would dominate the suite's runtime. The strides are coprime with the
            // instruction lengths, so cut points still land mid-instruction across the file.
            byte[] full = File.ReadAllBytes(GoldenFile.FixturePath(fixture));
            for (int len = 0; len <= full.Length; len += stride)
            {
                var slice = new byte[len];
                Array.Copy(full, slice, len);
                RenderAllEntryPoints("truncated " + fixture + " at " + len, slice);
            }
        }

        // ---- Mutation --------------------------------------------------------

        [Theory]
        [InlineData("test.plt")]
        [InlineData("feature-exercise.plt")]
        [InlineData("test-print.pcl")]
        public void Mutation_RandomByteCorruption_IsWellBehaved(string fixture)
        {
            // Seeded, so a failure is reproducible from the test name alone: the same
            // fixture and seed regenerate the exact byte sequence that broke it.
            const int Seed = 20260802;
            const int Rounds = 150;

            byte[] original = File.ReadAllBytes(GoldenFile.FixturePath(fixture));
            var rng = new Random(Seed);

            for (int round = 0; round < Rounds; round++)
            {
                byte[] mutated = (byte[])original.Clone();

                // Mix of the corruption modes a dropping bus actually produces: whole-byte
                // replacement, single-bit flips, and zero fills.
                int edits = 1 + rng.Next(12);
                for (int e = 0; e < edits; e++)
                {
                    int at = rng.Next(mutated.Length);
                    switch (rng.Next(3))
                    {
                        case 0: mutated[at] = (byte)rng.Next(256); break;
                        case 1: mutated[at] ^= (byte)(1 << rng.Next(8)); break;
                        default: mutated[at] = 0; break;
                    }
                }

                RenderAllEntryPoints(fixture + " mutated (seed " + Seed + ", round " + round + ")", mutated);
            }
        }

        [Theory]
        [InlineData("test.plt")]
        [InlineData("feature-exercise.plt")]
        public void Mutation_ByteDropsAndInsertions_IsWellBehaved(string fixture)
        {
            // Dropped bytes are the characteristic GPIB failure; insertions cover the
            // framing errors that produce duplicated bytes.
            const int Seed = 815;
            byte[] original = File.ReadAllBytes(GoldenFile.FixturePath(fixture));
            var rng = new Random(Seed);

            for (int round = 0; round < 100; round++)
            {
                var buffer = new List<byte>(original);
                int edits = 1 + rng.Next(20);
                for (int e = 0; e < edits && buffer.Count > 1; e++)
                {
                    int at = rng.Next(buffer.Count);
                    if (rng.Next(2) == 0) buffer.RemoveAt(at);
                    else buffer.Insert(at, (byte)rng.Next(256));
                }

                RenderAllEntryPoints(fixture + " drop/insert (seed " + Seed + ", round " + round + ")",
                    buffer.ToArray());
            }
        }

        // ---- Explicitly hostile inputs ---------------------------------------

        public static IEnumerable<object[]> HostileInputs()
        {
            yield return Case("empty", "");
            yield return Case("no terminator", "IN;SP1;PU100,100;PD200");
            yield return Case("absurd SI", "IN;SP1;SI1e30,1e30;PU100,100;LBhello;");
            yield return Case("absurd negative SI", "IN;SP1;SI-1e30,-1e30;PU100,100;LBhello;");
            yield return Case("absurd SC", "IN;SP1;SC0,1e308,0,1e308;PU100,100;PD200,200;");
            yield return Case("degenerate SC", "IN;SP1;SC5,5,5,5;PU100,100;PD200,200;");
            yield return Case("absurd IP", "IN;SP1;IP-2147483648,-2147483648,2147483647,2147483647;PU0,0;PD10,10;");
            yield return Case("NaN coordinates", "IN;SP1;PUNaN,NaN;PDInfinity,Infinity;");
            yield return Case("huge coordinates", "IN;SP1;PU1e308,1e308;PD-1e308,-1e308;");
            yield return Case("truncated PE escape run", "IN;SP1;PU100,100;PE" + (char)65 + (char)66);
            yield return Case("PE flags only", "IN;SP1;PU100,100;PE7<=>:;");
            yield return Case("PE huge values", "IN;SP1;PU100,100;PE" + new string('~', 400) + ";");
            yield return Case("huge CI radius", "IN;SP1;PU500,500;CI1e30;");
            yield return Case("CI tiny chord angle", "IN;SP1;PU500,500;CI500,0.0000001;");
            yield return Case("AA huge sweep", "IN;SP1;PU100,100;AA200,200,1e12,0.0000001;");
            yield return Case("EW degenerate", "IN;SP1;PU500,500;EW0,0,0;");
            yield return Case("FT tiny spacing", "IN;SP1;FT3,1e-9,0;PU100,100;RA5000,5000;");
            yield return Case("FT negative spacing", "IN;SP1;FT3,-50,0;PU100,100;RA5000,5000;");
            yield return Case("unterminated polygon mode", "IN;SP1;PM0;PD100,100;PD200,200;");
            yield return Case("FP with no polygon", "IN;SP1;FP;EP;");
            yield return Case("unclosed IW", "IN;SP1;IW100,100,50,50;PU0,0;PD1000,1000;");
            yield return Case("RO absurd angle", "IN;SP1;RO12345;PU100,100;PD200,200;");
            yield return Case("LB without terminator", "IN;SP1;PU10,10;LBnever ends");
            yield return Case("deep PM chain", "IN;SP1;PM0;" + Repeat("PD{0},{0};", 500) + "PM2;FP;");
            yield return Case("many nested PM cycles", "IN;SP1;" + Repeat("PM0;PD10,10;PM2;FP;", 200));
            yield return Case("very long label", "IN;SP1;PU10,10;LB" + new string('A', 200000) + (char)3);
            yield return Case("many pen changes", "IN;SP1;" + Repeat("SP{0};PD{0},{0};", 2000));
            yield return Case("only separators", new string(';', 100000));
            yield return Case("binary garbage", BinaryGarbage(4096));
        }

        private static object[] Case(string name, string hpgl) => new object[] { name, hpgl };

        private static string Repeat(string format, int count)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= count; i++) sb.AppendFormat(format, i);
            return sb.ToString();
        }

        private static string BinaryGarbage(int length)
        {
            var rng = new Random(99);
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append((char)rng.Next(1, 256));
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(HostileInputs))]
        public void HostileInput_IsWellBehaved(string name, string hpgl)
        {
            RenderAllEntryPoints("hostile input '" + name + "'", Latin1(hpgl));
        }

        // ---- Output-size requests --------------------------------------------

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, -1)]
        [InlineData(10, -5)]
        [InlineData(-5, 10)]
        [InlineData(1, 1)]
        [InlineData(int.MaxValue, int.MaxValue)]
        [InlineData(100000, 100000)]
        public void OutputSize_DegenerateRequests_AreWellBehaved(int width, int height)
        {
            // Zero, negative and unallocatably-large canvases must be rejected as argument
            // errors rather than crashing. (Whether the message is helpful is #13's
            // business - today these surface GDI+'s own "Parameter is not valid.")
            byte[] plot = Latin1("IN;SP1;PU100,100;PD900,900;");
            var options = new HpglRenderOptions { Width = width, Height = height };

            RenderMustBeWellBehaved("RenderToPng at " + width + "x" + height,
                () => HpglRenderer.RenderToPng(plot, options));
            RenderMustBeWellBehaved("RenderToSvg at " + width + "x" + height,
                () => HpglRenderer.RenderToSvg(plot, options));
        }

        // ---- Null inputs -----------------------------------------------------

        [Fact]
        public void NullInputs_AreWellBehaved()
        {
            RenderMustBeWellBehaved("RenderToPng(null bytes)", () => HpglRenderer.RenderToPng((byte[])null));
            RenderMustBeWellBehaved("RenderToSvg(null bytes)", () => HpglRenderer.RenderToSvg((byte[])null));
            RenderMustBeWellBehaved("RenderToPng(null string)", () => HpglRenderer.RenderToPng((string)null));
            RenderMustBeWellBehaved("RenderToSvg(null string)", () => HpglRenderer.RenderToSvg((string)null));
            RenderMustBeWellBehaved("HpglTraceRepair.Repair(null)", () => HpglTraceRepair.Repair(null));
            RenderMustBeWellBehaved("PclRasterRepair.Repair(null)", () => PclRasterRepair.Repair(null));
            RenderMustBeWellBehaved("PclRenderer.LooksLikePcl(null)", () => PclRenderer.LooksLikePcl(null));
            RenderMustBeWellBehaved("PclRenderer.RenderToPng(null)", () => PclRenderer.RenderToPng(null));
            RenderMustBeWellBehaved("ScreenImage.ToPng(null)", () => ScreenImage.ToPng(null));
            RenderMustBeWellBehaved("ScreenImage.Dimensions(null)", () =>
            {
                int w, h;
                ScreenImage.Dimensions(null, out w, out h);
            });
            RenderMustBeWellBehaved("UnsupportedTypography(null)", () => HpglRenderer.UnsupportedTypography(null));
            RenderMustBeWellBehaved("HasCorruptCoordinate(null)", () =>
            {
                string detail;
                HpglRenderer.HasCorruptCoordinate(null, out detail);
            });
        }

        // ---- Regression corpus -----------------------------------------------

        /// <summary>
        /// Replays every file in tests/fixtures/corpus. The corpus is the permanent record
        /// of inputs that once broke something: when a fuzz round or a bug report finds a
        /// crash, the exact bytes are committed here so the fix cannot silently regress.
        ///
        /// It is EMPTY today, and that is a real result rather than an oversight - the
        /// sweeps in this file (roughly 2,600 renders across truncation, mutation and the
        /// hostile set) found no input that violates the contract. The mechanism exists so
        /// that the first one found is one file away from being a permanent test.
        /// </summary>
        [Fact]
        public void Corpus_EveryCheckedInCrasher_IsWellBehaved()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "corpus");
            if (!Directory.Exists(dir)) return;

            foreach (string file in Directory.GetFiles(dir))
            {
                if (Path.GetFileName(file).Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
                RenderAllEntryPoints("corpus/" + Path.GetFileName(file), File.ReadAllBytes(file));
            }
        }
    }
}
