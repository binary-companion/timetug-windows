# Tray digit glyphs

`digits.txt` holds the digits 1 to 9 that the tray icon shows in the last ten minutes before a meeting. The shapes are
original work made for TimeTug (a few lines per digit on a 5 by 7 grid, scaled to four icon sizes), so no font and no
font licence is involved, and the drawing does not depend on the fonts of the machine that runs it. The file is covered
by the repository licence like the rest of the code.

- `TimeTug.Platform` embeds this file and draws the digits itself (`TrayGlyphSet`, `TrayIconRenderer`); nothing is read from
  Windows. The result is the same on every machine, which is what lets the golden images in
  `tests/TimeTug.Platform.Tests/data` be compared.
- To change a shape, edit the polylines here and refresh the golden images with
  `scripts/ci/tray-goldens.ps1 -Update`; review the new pictures and the contact sheet in `docs/screenshots` before committing.
- The sizes (16, 20, 24 and 32 px) are the notification-area icon sizes at 100, 125, 150 and 200 percent display scaling.
