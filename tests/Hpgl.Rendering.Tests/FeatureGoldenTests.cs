// -----------------------------------------------------------------------------
// Per-feature golden coverage for the HP-GL instruction families most likely to
// drift silently.
//
// Why SVG snapshots rather than image goldens for most of these: the SVG writer
// emits integer coordinates only, so its output is exactly reproducible and a
// regression shows up as a readable line diff ("this arc now has 8 chords, not
// 32") instead of a pixel percentage that says only "something moved". Raster
// goldens are used where raster IS the output under test - the PCL path, and the
// whole-plot renders.
//
// Each plot below is small and single-purpose on purpose. The existing
// feature-exercise.plt drives everything at once, which proves the pipeline runs
// but cannot say WHICH family broke.
//
// The HP-GL plotter-emulation technique is derived from the HP7470A Plotter
// Emulator (7470.cpp) by John Miles, KE5FX - http://www.ke5fx.com/
// -----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using Hpgl.Rendering;
using Xunit;

namespace Hpgl.Rendering.Tests
{
    public class FeatureGoldenTests
    {
        private static byte[] Latin1(string s) => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

        private static string Svg(string hpgl, HpglRenderOptions options = null) =>
            HpglRenderer.RenderToSvg(Latin1(hpgl), options);

        // ---- Line types: LT0-LT6 dash patterns and phase --------------------

        [Fact]
        public void LineTypes_AllPatterns_MatchSnapshot()
        {
            // Each pattern on its own row, same length and start x, so a phase or
            // dash-length regression shows as a changed run on one row only.
            var sb = new StringBuilder("IN;SP1;");
            for (int lt = 0; lt <= 6; lt++)
                sb.Append("LT").Append(lt).Append(";PU500,").Append(1000 + lt * 400)
                  .Append(";PD5500,").Append(1000 + lt * 400).Append(';');
            sb.Append("LT;PU500,400;PD5500,400;"); // LT with no argument == solid
            GoldenFile.AssertSvgMatches("lt-dash-patterns", Svg(sb.ToString()));
        }

        // ---- Fills: FT solid / hatch / cross --------------------------------

        [Fact]
        public void FillTypes_SolidHatchCross_MatchSnapshot()
        {
            const string hpgl =
                "IN;SP1;" +
                "FT1;PU500,500;RA2000,2000;" +            // solid
                "FT3,80,0;PU2500,500;RA4000,2000;" +      // hatch, 0 degrees
                "FT3,80,45;PU4500,500;RA6000,2000;" +     // hatch, 45 degrees
                "FT4,80,0;PU6500,500;RA8000,2000;" +      // cross-hatch
                "FT2;PU500,2500;RA2000,4000;";            // FT2 == solid (alternate form)
            GoldenFile.AssertSvgMatches("ft-fill-types", Svg(hpgl));
        }

        // ---- Polygon mode: PM / FP / EP and even-odd holes ------------------

        // An outer rectangle with an inner contour that should read as a hole. Both fill
        // types are pinned because the library treats them differently *by design*:
        // FillContours sends FT3/FT4 through the even-odd scanline hatcher, but sends
        // FT1/FT2 to the sink's per-contour polygon fill, which cannot subtract a hole
        // (see the "holes not subtracted" note in HpglRenderer.FillContours). Pinning
        // both means a change to either path is visible, and the asymmetry itself is
        // recorded rather than assumed. Closing that gap is tracked separately.
        private const string PolygonWithHole =
            "PU1000,1000;PM0;" +
            "PD5000,1000;PD5000,4000;PD1000,4000;PD1000,1000;" +
            "PU2000,2000;PD4000,2000;PD4000,3000;PD2000,3000;PD2000,2000;" +
            "PM2;FP;SP2;EP;";

        [Fact]
        public void PolygonMode_SolidFill_DoesNotSubtractHole_MatchSnapshot()
        {
            // Documents current behaviour: two independently filled contours, so the inner
            // rectangle is painted over rather than punched out.
            string svg = Svg("IN;SP1;FT1;" + PolygonWithHole);
            Assert.Equal(2, CountOccurrences(svg, "<polygon"));
            GoldenFile.AssertSvgMatches("pm-solid-fill-no-hole", svg);
        }

        [Fact]
        public void PolygonMode_HatchFill_HonoursEvenOddHole_MatchSnapshot()
        {
            // The hatcher does respect even-odd, so hatch lines must stop at the inner
            // contour and resume past it. A fill-rule regression shows as spans crossing
            // the hole.
            GoldenFile.AssertSvgMatches("pm-hatch-fill-hole", Svg("IN;SP1;FT3,80,0;" + PolygonWithHole));
        }

        private static int CountOccurrences(string haystack, string needle) =>
            haystack.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

        // ---- Clipping: IW ----------------------------------------------------

        [Fact]
        public void ClipWindow_ClipsCircleAndLines_MatchSnapshot()
        {
            // A circle and a crossing line, each drawn once unclipped and once through
            // a window that cuts them. A clipping regression changes the clipped copy
            // while leaving the unclipped one identical, which localises the fault.
            const string hpgl =
                "IN;SP1;PU2000,2000;CI1200;" +
                "PU500,500;PD4500,4500;" +
                "SP2;IW1500,1500,2500,2500;" +
                "PU2000,2000;CI1200;" +
                "PU500,500;PD4500,4500;" +
                "IW;";
            GoldenFile.AssertSvgMatches("iw-clip-window", Svg(hpgl));
        }

        // ---- Rotation: RO at all four angles ---------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void Rotation_AllFourAngles_MatchSnapshot(int angle)
        {
            // An deliberately asymmetric figure - a right-angle plus a tick - so that a
            // rotation that lands on the wrong quadrant cannot accidentally match.
            string hpgl =
                "IN;SP1;RO" + angle + ";" +
                "PU1000,1000;PD5000,1000;PD5000,3000;" +
                "PU1000,1000;PD1000,1500;" +
                "PU4500,1000;PD4500,1200;";
            GoldenFile.AssertSvgMatches("ro-" + angle, Svg(hpgl));
        }

        // ---- Encoded polylines: PE -------------------------------------------

        /// <summary>
        /// Encodes values into an HP-GL/2 PE payload, base-32 (the <c>7</c> flag form):
        /// zig-zag signed, little-endian base-32 digits, non-terminating characters at
        /// 63 + digit and the terminating character at 95 + digit.
        /// </summary>
        private static string PeBase32(params int[] values)
        {
            var sb = new StringBuilder("7");
            foreach (int v in values)
            {
                int acc = v >= 0 ? 2 * v : -2 * v + 1;   // zig-zag
                while (acc >= 32)
                {
                    sb.Append((char)(63 + (acc & 31)));
                    acc >>= 5;
                }
                sb.Append((char)(95 + acc));             // terminator carries the high digit
            }
            return sb.ToString();
        }

        [Fact]
        public void EncodedPolyline_Base32_MatchesEquivalentPenPath()
        {
            // The strongest assertion available for PE: the decoded polyline must render
            // identically to the same geometry written as plain PU/PD. This compares the
            // decoder against the rest of the pipeline rather than against itself.
            string encoded =
                "IN;SP1;PU1000,1000;PE" + PeBase32(2000, 1500, 1000, -900, -1500, -300) + ";";
            string plain =
                "IN;SP1;PU1000,1000;PD3000,2500;PD4000,1600;PD2500,1300;";

            Assert.Equal(Svg(plain), Svg(encoded));
        }

        [Fact]
        public void EncodedPolyline_Base32_MatchSnapshot()
        {
            // NOTE: base-32 only, deliberately. The default base-64 PE mode currently
            // mis-decodes its terminating characters (issue #46) - committing a base-64
            // golden now would freeze that defect into the regression suite. The base-64
            // golden lands with the fix.
            string hpgl = "IN;SP1;PU1000,1000;PE" +
                          PeBase32(2000, 1500, 1000, -900, -1500, -300, 800, 1200) + ";";
            GoldenFile.AssertSvgMatches("pe-base32-polyline", Svg(hpgl));
        }

        // ---- Labels: stroke font and SvgTextLabels ---------------------------

        [Fact]
        public void StrokeFontLabels_HighFidelity_MatchSnapshot()
        {
            // Default rendering strokes each glyph as vectors from the KE5FX table, so
            // this snapshot is the font's regression guard - it is the same data the
            // generator drift check (#37) protects on the input side.
            const string hpgl =
                "IN;SP1;SI0.3,0.4;PU500,3000;LBABCDEFGHIJKLM;" +
                "PU500,2400;LBnopqrstuvwxyz;" +
                "PU500,1800;LB0123456789 .,:;-+*/=()[];" +
                "SL0.4;PU500,1200;LBslanted;SL0;" +
                "DI0,1;PU5000,600;LBvertical;DI1,0;" +
                "SR3,4;PU500,600;LBscaled;";
            GoldenFile.AssertSvgMatches("stroke-font-labels", Svg(hpgl));
        }

        [Fact]
        public void SvgTextLabels_EmitsSelectableText_MatchSnapshot()
        {
            // The opposite trade-off: real <text> elements instead of stroked outlines,
            // so the SVG is selectable and searchable but depends on a viewer font.
            const string hpgl =
                "IN;SP1;SI0.3,0.4;PU500,3000;LBSelectable Text;" +
                "PU500,2400;LB0123456789;" +
                "DI0,1;PU5000,600;LBrotated;DI1,0;";
            var options = new HpglRenderOptions { SvgTextLabels = true };

            string svg = Svg(hpgl, options);
            Assert.Contains("<text", svg);
            GoldenFile.AssertSvgMatches("svg-text-labels", svg);
        }

        // ---- Whole-plot raster goldens ---------------------------------------

        [Fact]
        public void FeatureExercise_RasterRender_MatchesGolden()
        {
            // The all-instructions plot as an image. The per-feature snapshots above say
            // WHICH family broke; this says whether the composed result still looks right,
            // including the parts only GDI+ decides (stroke joins, fill rasterisation).
            byte[] hpgl = File.ReadAllBytes(GoldenFile.FixturePath("feature-exercise.plt"));
            GoldenFile.AssertPngMatches("feature-exercise", HpglRenderer.RenderToPng(hpgl));
        }

        [Fact]
        public void FeatureExercise_Svg_MatchSnapshot()
        {
            byte[] hpgl = File.ReadAllBytes(GoldenFile.FixturePath("feature-exercise.plt"));
            GoldenFile.AssertSvgMatches("feature-exercise", HpglRenderer.RenderToSvg(hpgl));
        }

        [Fact]
        public void PclPrint_RasterRender_MatchesGolden()
        {
            // The PCL raster path had no image-level regression at all: LooksLikePcl and
            // the decoder were covered, but nothing asserted what came out the far end.
            byte[] pcl = File.ReadAllBytes(GoldenFile.FixturePath("test-print.pcl"));
            Assert.True(PclRenderer.LooksLikePcl(pcl), "fixture should be detected as PCL");
            GoldenFile.AssertPngMatches("pcl-print", PclRenderer.RenderToPng(pcl));
        }
    }
}
