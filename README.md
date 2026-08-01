# Hpgl.Rendering

[![CI](https://github.com/TGoodhew/Hpgl.Rendering/actions/workflows/ci.yml/badge.svg)](https://github.com/TGoodhew/Hpgl.Rendering/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Hpgl.Rendering.svg)](https://www.nuget.org/packages/Hpgl.Rendering/)

A small, standalone C# library that turns a **plotter or printer stream captured from a
test instrument** into an image. Point it at the HP-GL/2 a spectrum analyzer emits for a
hardcopy, or at a PCL "print" dump, and get back a `Bitmap`, PNG bytes, or a
self-contained SVG document.

It has no GPIB, VISA or instrument-control dependencies — it does one job, on bytes you
have already captured.

```csharp
using Hpgl.Rendering;

byte[] png = HpglRenderer.RenderToPng(hpglText, new HpglRenderOptions
{
    Width      = 1280,
    Height     = 960,
    Background = HpglBackground.Black,
});

string svg = HpglRenderer.RenderToSvg(hpglBytes);   // resizable, no raster

// PCL "print" streams are auto-detected and handled the same way
if (PclRenderer.LooksLikePcl(bytes))
    png = PclRenderer.RenderToPng(bytes);
```

## Install

```
dotnet add package Hpgl.Rendering
```

**Targets:** `net472` and `net8.0-windows`.

`net8.0-windows` rather than plain `net8.0` is deliberate: rendering goes through
`System.Drawing.Common`, which is **Windows-only from .NET 6 onward** (it throws on Linux
and macOS, and .NET 7 removed the compatibility switch). The target framework states that
constraint up front instead of failing at run time. On `net472` there are no external
package dependencies at all — just the framework's own `System.Drawing`.

## What it handles

### HP-GL/2

Geometry is auto-fit (aspect-preserving) to the output canvas, so any stream renders
without the caller knowing the source plot bounds. Coverage follows the "minimum-viable"
instruction set in [`docs/HPGL-7475A-7550A-Rendering-Spec.md`](docs/HPGL-7475A-7550A-Rendering-Spec.md):

- **Configuration / coordinates:** `IN` / `DF`, `IP`, `SC`, `RO` (0/90/180/270),
  `IW` (soft-clip window — geometrically clips vectors, fills and label strokes).
- **Vectors:** `SP` (pen select → colour), `PU` / `PD` / `PA` / `PR`.
- **Curves & rectangles:** `CI`, `AA` / `AR`, `EW`, `EA` / `ER` — chord-subdivided to the
  `CT` parameter.
- **Area fill:** `RA` / `RR`, `WG`, `FT` (solid, parallel hatch, cross-hatch), `PT`,
  `UF` (user-defined variable-spacing hatch). Solid fills use a native polygon fill;
  hatch and cross are emitted as scanline line-spans.
- **7550A polygons:** `PM` / `EP` / `FP` with even-odd multi-contour fill (holes).
- **Line types & ticks:** `LT` as dash/dot patterns (4 % of the diagonal by default);
  `TL` / `XT` / `YT`.
- **Encoded polylines:** `PE` (base-64/32 relative coordinates, including the
  `7` / `<` / `=` / `>` / `:` flags).
- **Labels / text**, drawn as real vector strokes so they honour size, slant, direction,
  rotation, clipping, pen colour and line type: `LB`, `DT`, `SI` / `SR` (including
  mirroring via negative size), `SL`, `DI` / `DR`, `CP`, `ES`, `SM`, `CS` / `CA` / `SS` /
  `SA`, in-label shift-in/out, and embedded CR/LF for multi-line labels.

### PCL

`PclRenderer` decodes the byte-raster subset instruments actually emit for a "print"
(`ESC*b<n>W` rows and friends) and rasterizes it. `PclRenderer.LooksLikePcl` lets a
caller route a captured stream without guessing.

### Stream repair

Captures taken over a real GPIB bus can lose a byte at a read seam. Two repair passes
are included, both compression-agnostic and safe to run on clean input:

- `HpglTraceRepair.Repair` — fixes corrupt coordinate runs in HP-GL text.
- `PclRasterRepair.Repair` — re-frames PCL raster rows to their declared byte count, so a
  dropped byte doesn't cascade into the printer reading the next row's `ESC` as raster
  data. Recovers the framing; the affected scanline may still be a few pixels off, since
  the dropped byte's position within its row is unknowable.

### Instruments that return a screenshot instead

Not every hardcopy arrives as a vector stream. Some instruments hand back their screen
directly as an image via a SCPI query (Rigol's `:DISP:DATA?`, for example), with nothing
to render. `ScreenImage` normalises those bytes so a caller handling both kinds of capture
still ends up with one output format:

```csharp
byte[] png = ScreenImage.ToPng(imageBytes);            // BMP/GIF/JPEG/PNG in, PNG out
ScreenImage.Dimensions(imageBytes, out int w, out int h);
```

A Rigol BMP is ~1.1 MB uncompressed; the PNG is a fraction of that. Both methods throw
`ArgumentException` on null or empty input.

### Displaying the output

**WinForms** takes the `Bitmap` directly:

```csharp
pictureBox.Image = HpglRenderer.RenderToBitmap(hpgl);   // caller owns it - dispose it
```

**WPF** will not accept a `System.Drawing.Bitmap`. Go via the PNG bytes rather than
`CreateBitmapSourceFromHBitmap`, which leaks the HBITMAP unless you `DeleteObject` it:

```csharp
var image = new BitmapImage();
image.BeginInit();
image.StreamSource = new MemoryStream(HpglRenderer.RenderToPng(hpgl));
image.CacheOption  = BitmapCacheOption.OnLoad;   // decode now, so the stream can be freed
image.EndInit();
image.Freeze();                                  // safe to hand to the UI thread
```

`CacheOption = OnLoad` is not optional: without it `BitmapImage` decodes lazily and holds
the stream open. The same applies to the raw `Bitmap` path — `new Bitmap(stream)` requires
the stream to outlive the bitmap, which is why `ScreenImage` copies into a stream it owns.

**SVG has no built-in path.** Neither `System.Drawing` nor WPF can display SVG; that needs
a third-party rasterizer (SharpVectors, Svg.Skia) or a WebView. If you want pixels, call
`RenderToBitmap` / `RenderToPng` — they rasterize the same geometry directly. `RenderToSvg`
is for when the output should stay resolution-independent.

### Deliberately out of scope

The interactive / live-bus instructions return data to the controller or drive hardware
and produce no geometry, so they are parsed and skipped — a stream containing them still
renders: the output/digitize set (`OA`/`OC`/`OH`/`OI`/`OS`/`OW`/`OE`, `DC`/`DP`), `ESC .`
device-control escapes, pen dynamics (`VS`/`FS`/`AS`/`AP`/`CV`), and page/replot/memory
(`PG`/`AF`/`AH`/`RP`/`WD`/`GM`/`KY`). A faithful interactive emulator — answering `OE`/`OS`,
an error register, exact per-model numeric ranges — would be a separate project.

Not yet supported: `UC` user-defined characters and `DL` downloadable glyphs; the 7550A
slot/encoding model (`DS`/`IV`/`CM`, 7/8-bit + ISO, linked Roman8/Katakana8) and non-ASCII
international sets; and buffered labels (`BL`/`PB`/`OL`).

Unlike `7470.cpp` this renderer carries **no per-instrument fix-ups**. Those belong in the
caller's capture profile, not in a general renderer.

## The built-in font

HP's internal glyph outlines were never published as coordinate tables, so labels are
drawn with a single-stroke vector font for ASCII Set 0
([`StrokeFont.cs`](src/Hpgl.Rendering/StrokeFont.cs)), generated from the KE5FX vector
character table by [`tools/ke5fx_font/generate_strokefont.py`](tools/ke5fx_font/). It is
generated — regenerate it rather than hand-editing.

The font is **fixed-pitch**, like a real HP plotter. The per-character pitch is `1.375 ×`
the HP-GL character width (`SI`/`SR`), which is the cell ratio of HP's built-in stick
font. That isn't a magic constant: HP instruments lay annotations on a character grid,
placing each field with an absolute `PA` at an integer number of cells, and the grid step
is exactly `1.375 ×` the `SI`/`SR` width. Matching it makes the renderer honour the grid
the *stream itself* defines, so columns in adjacent rows line up — `CENTER` over `*RBW`
puts `R` under `C` and `B` under `E`. The ratio is dimensionless and applies at any size.

Geometry is faithful; exact glyph shapes are an approximation, so label text will not
match a real plotter glyph-for-glyph.

## Building

```
dotnet build Hpgl.Rendering.sln -c Release
dotnet test  Hpgl.Rendering.sln -c Release
```

Tests run against **both** target frameworks — 113 each. `tests/fixtures/` holds real
captures: an HP 8563E plot with its golden render, a real PCL print dump, and
`feature-exercise.plt`, a hand-authored plot that drives every supported instruction and
makes a good visual sanity check.

### Coverage

CI measures coverage on every push and pull request and fails below the thresholds in
[`eng/Check-Coverage.ps1`](eng/Check-Coverage.ps1) — currently **90 % line, 79 % branch**,
against a measured 90.6 % / 80.0 %. Lowering a threshold means editing that file, so it
shows up in review instead of drifting.

To reproduce the CI number locally:

```
dotnet tool restore
dotnet test Hpgl.Rendering.sln -c Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet reportgenerator "-reports:tests/**/coverage.cobertura.xml" "-targetdir:artifacts/coverage" "-reporttypes:Html;Cobertura;TextSummary"
pwsh eng/Check-Coverage.ps1
```

Then open `artifacts/coverage/index.html`. CI publishes that same HTML as a build
artifact named `coverage`, and writes the summary table into the run's job summary.

The gate runs against the **merged** report. Both target frameworks execute the same
source through different runtimes, so a line covered on one is covered by the suite;
gating each separately would demand redundant tests to satisfy an artefact of
multi-targeting. `StrokeFont.cs` is excluded — it is generated glyph data rather than
logic, and its correctness is asserted behaviourally by the font-metrics and
label-rendering tests.

Versions come from git tags via [MinVer](https://github.com/adamralph/minver): tag `v1.2.3`
and the package builds as `1.2.3`. Untagged builds are `0.0.0-alpha.0.N`.

To try a package build against a consumer before publishing, `./pack-local.ps1` packs to a
local feed folder — see the script header for wiring it into a consuming project.

## Credit

The HP-GL plotter-emulation **capture-and-render technique** this library supports is
derived from the **HP7470A Plotter Emulator (`7470.cpp`) by John Miles, KE5FX**, and the
built-in stroke font is generated from that project's vector character table (John Miles
and Mark S. Sims).

> Original C++ author: **John Miles (KE5FX)** — <http://www.ke5fx.com/>

This C# library is an independent adaptation and carries no warranty from KE5FX. Please
keep this attribution in derivative work.

## License

MIT — see [LICENSE](LICENSE).
