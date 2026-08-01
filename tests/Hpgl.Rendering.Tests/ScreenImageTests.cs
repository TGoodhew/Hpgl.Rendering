using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Hpgl.Rendering;
using Xunit;

namespace Hpgl.Rendering.Tests
{
    public class ScreenImageTests
    {
        /// <summary>A screenshot-like image (dark bg + grid + bright traces) that compresses well, like a scope screen.</summary>
        private static Bitmap MakeScreenshot(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                using (var grid = new Pen(Color.FromArgb(40, 80, 40)))
                    for (int x = 0; x <= w; x += w / 10)
                    {
                        g.DrawLine(grid, x, 0, x, h);
                        g.DrawLine(grid, 0, x % h, w, x % h);
                    }
                using (var trace = new Pen(Color.Lime, 2))
                    for (int x = 0; x < w - 4; x += 4)
                        g.DrawLine(trace, x, h / 2 + (x % 60) - 30, x + 4, h / 2 + ((x + 4) % 60) - 30);
            }
            return bmp;
        }

        private static byte[] Encode(Bitmap b, ImageFormat fmt)
        {
            using (var ms = new MemoryStream()) { b.Save(ms, fmt); return ms.ToArray(); }
        }

        [Fact]
        public void ToPng_ConvertsBmp_ToPngAndShrinksIt()
        {
            using (var src = MakeScreenshot(800, 480))
            {
                byte[] bmp = Encode(src, ImageFormat.Bmp);
                byte[] png = ScreenImage.ToPng(bmp);

                // PNG magic
                Assert.True(png.Length > 8 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47);
                // A solid-ish screenshot PNG is far smaller than the uncompressed BMP.
                Assert.True(png.Length < bmp.Length, "PNG (" + png.Length + ") should be smaller than BMP (" + bmp.Length + ")");
            }
        }

        [Fact]
        public void Dimensions_ReportsSourceSize()
        {
            using (var src = MakeScreenshot(640, 360))
            {
                ScreenImage.Dimensions(Encode(src, ImageFormat.Png), out int w, out int h);
                Assert.Equal(640, w);
                Assert.Equal(360, h);
            }
        }

        // The XML docs promise ArgumentException on empty input; pin that so it stays part of the contract.
        [Theory]
        [InlineData(null)]
        [InlineData(new byte[0])]
        public void ToPng_ThrowsArgumentException_OnMissingData(byte[] bytes)
        {
            Assert.Throws<ArgumentException>(() => ScreenImage.ToPng(bytes));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(new byte[0])]
        public void Dimensions_ThrowsArgumentException_OnMissingData(byte[] bytes)
        {
            Assert.Throws<ArgumentException>(() => ScreenImage.Dimensions(bytes, out _, out _));
        }
    }
}
