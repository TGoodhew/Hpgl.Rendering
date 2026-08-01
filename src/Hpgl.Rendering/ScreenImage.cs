// -----------------------------------------------------------------------------
// Hpgl.Rendering - instrument screen-image normalisation.
//
// Not every instrument hardcopy arrives as HP-GL/PCL. Some return the screen
// directly as an image (PNG/BMP) via a SCPI query - e.g. Rigol :DISP:DATA? - with
// no vector rendering needed. A caller that handles both kinds of capture still
// wants one output format, so this normalises those bytes to PNG and reports their
// dimensions; the rest of the library turns vector streams into the same thing.
//
// Deliberately NOT here: building a size-bounded inline preview for a chat/agent
// artifact. That was inherited from the GPIB-MCP host this library was extracted
// from - it was parameterised by that host's artifact character budget - and it
// belongs in the host, not in a rendering package (see issue #30).
// -----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Hpgl.Rendering
{
    /// <summary>
    /// Normalises an instrument screen image (a screenshot returned directly by the instrument,
    /// rather than an HP-GL/PCL stream) to PNG, and reports its pixel dimensions.
    /// </summary>
    public static class ScreenImage
    {
        /// <summary>
        /// Decodes any supported instrument image (PNG, BMP, GIF, JPEG) and re-encodes it as PNG - so a
        /// bulky uncompressed BMP (Rigol returns ~1.1 MB) becomes a compact, universally-viewable PNG.
        /// </summary>
        /// <param name="imageBytes">The encoded image as returned by the instrument.</param>
        /// <returns>The same image encoded as PNG.</returns>
        /// <exception cref="ArgumentException"><paramref name="imageBytes"/> is null or empty.</exception>
        public static byte[] ToPng(byte[] imageBytes)
        {
            using (var bmp = Load(imageBytes))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>Pixel dimensions of an instrument image, for reporting (out-params keep callers System.Drawing-free).</summary>
        /// <param name="imageBytes">The encoded image as returned by the instrument.</param>
        /// <param name="width">Receives the image width in pixels.</param>
        /// <param name="height">Receives the image height in pixels.</param>
        /// <exception cref="ArgumentException"><paramref name="imageBytes"/> is null or empty.</exception>
        public static void Dimensions(byte[] imageBytes, out int width, out int height)
        {
            using (var bmp = Load(imageBytes)) { width = bmp.Width; height = bmp.Height; }
        }

        private static Bitmap Load(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("no image data", nameof(imageBytes));
            // Copy into an owned stream Bitmap can keep; new Bitmap(stream) requires the stream to stay open.
            var ms = new MemoryStream();
            ms.Write(imageBytes, 0, imageBytes.Length);
            ms.Position = 0;
            return new Bitmap(ms);
        }
    }
}
