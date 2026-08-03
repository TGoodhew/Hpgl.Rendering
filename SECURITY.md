# Security Policy

## Supported versions

Nothing has been released yet — there is no `v*` tag and no package on nuget.org, so the
only supported code is the current `main` branch.

Once 1.0.0 ships this table becomes the policy:

| Version | Supported |
| --- | --- |
| Latest 1.x | Yes |
| Older 1.x | Fixes land in the latest 1.x only; upgrade to receive them |
| Pre-1.0.0 | No |

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Report it privately through GitHub Security Advisories:
[**Report a vulnerability**](https://github.com/TGoodhew/Hpgl.Rendering/security/advisories/new).
That creates a private thread visible only to you and the maintainer, and it is the only
supported reporting channel.

If GitHub is unavailable to you, email <tony@schnauzergroup.com> with `SECURITY` in the
subject.

### What to expect

This library is maintained by one person, so these are honest targets rather than a
contractual SLA:

| Stage | Target |
| --- | --- |
| Acknowledgement of your report | 7 days |
| Initial assessment — accepted, needs more information, or declined with reasons | 14 days |
| Fix released, or a public timeline if the work is larger | 90 days |

Credit is given in the advisory unless you ask otherwise. If a report is declined you will
be told why, and you are free to disclose publicly at that point.

## Scope

This library's job is to turn a byte stream captured from a test instrument into a bitmap
or an SVG. **That stream is untrusted input** — it arrives over a bus from a device that
may be faulty, may be misconfigured, or may not be the instrument you think it is. The
parser surface is therefore the security surface, and reports about it are in scope:

- Memory-safety or out-of-bounds behaviour reachable from a malformed or truncated stream,
  particularly the PCL raster decoder and the `PE` encoded-polyline path
- Unbounded allocation, non-terminating loops, or other denial of service triggered by a
  crafted stream rather than by a legitimately large plot
- Any path where rendering a stream writes outside the output file the caller named, or
  otherwise touches the filesystem or network — this library is not supposed to do either
- Vulnerabilities in the release and packaging pipeline that could affect a published
  package

Out of scope:

- Crashes caused by a caller passing invalid arguments to the public API. Argument
  validation is tracked as ordinary work in
  [#13](https://github.com/TGoodhew/Hpgl.Rendering/issues/13), not as a vulnerability.
- Resource use proportional to a legitimately large or complex plot.
- Vulnerabilities in `System.Drawing.Common`, GDI+, or the .NET runtime itself. Report
  those to their maintainers; if this library uses them in a way that makes an underlying
  issue exploitable when it otherwise would not be, that part is in scope here.
- Anything requiring an attacker who already controls the machine running the renderer.

## Hardening already in place

So you know what has been ruled out before you spend time on it: CI and release workflows
pin every third-party action to a full commit SHA and run with least-privilege tokens,
CodeQL runs the `security-extended` suite on every pull request and weekly, and `main`
requires both checks to pass before a merge. Release tags cannot be moved or deleted.
