# Service logos

The marks the accounts list shows beside an account and in the `+` menu (spec `2026-11-02-shared-accounts-list-design.md` in `timetug`, decision D19 and A1.5). They are **third-party marks**, not covered by this repository's licence or by `artwork/LICENSE.md`; `docs/third-party.md` lists them.

| File | Mark | Source | Checked |
|---|---|---|---|
| `microsoft.svg` | The Microsoft four-square logo: red `#F25022`, green `#7FBA00`, blue `#00A4EF`, yellow `#FFB900`; four 9 by 9 squares with a 1 unit gap (the proportions of the downloadable `ms-symbollockup_mssymbol_19.svg`) | Microsoft identity platform, "Sign in with Microsoft branding guidelines" (`learn.microsoft.com/entra/identity-platform/howto-add-branding-in-apps`) | 2026-11-02 |
| `google.svg` | The standard four-colour Google "G" (`#EA4335`, `#4285F4`, `#FBBC05`, `#34A853`), the path data of Google's published G logo | Google Identity, "Sign in with Google branding guidelines" (`developers.google.com/identity/branding-guidelines`) | 2026-11-02 |

Each file draws the mark on a 28 by 28 white rounded tile with a thin light-grey outline, so one asset serves the light, dark and high-contrast themes and the mark always sits on the white background Google requires. The marks themselves are never recoloured, scaled non-uniformly or redrawn.

## Brand rules we follow

- Microsoft: use the logo unaltered (no recolour, no rotation, no distortion); light or dark backgrounds are allowed.
- Google: the standard-colour G on a white background; no monochrome version, no other colours, do not draw our own G or use an outdated one; scale it uniformly.
- Both: the mark is decorative beside text that already names the service ("Microsoft", "Google"), and has an accessible name ("Microsoft logo", "Google logo") on an account row. A high-contrast theme changes nothing about the marks (neither vendor allows recolouring them); the tile is what keeps them legible.

## Open points for the owner

- Google's guidelines speak of "Sign in with Google" buttons. Using the G beside an account row and on the `+` entry is the closest allowed use (spec question Q12). If the owner prefers, replace `google.svg` by a generic glyph in `ProviderMarks`.
- The G path data was written from Google's published logo vector, not taken from the downloadable `signin-assets.zip` (a download needs the owner's go-ahead). Swapping in the G from that bundle changes nothing else: keep the 28 by 28 tile.
- The tile's outline is a fixed light grey (the spec's A1.5 asked for the WindowText colour in a high-contrast theme; an SVG cannot follow the theme, and the tile already gives the mark its contrast).

## Where they are used

`ProviderMarks` (Presentation) maps an account kind to a mark; `ProviderLogos` (App) loads these files as `SvgImageSource`; `AccountsListBox` shows them on rows and menu entries. Tests: `ProviderMarkTests`, and the self-tests `accounts-list`, `accounts-brokered` and `settings-accounts` assert that a row's logo is drawn and named.
