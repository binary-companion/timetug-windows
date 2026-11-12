# Third-party marks

TimeTug for Windows shows two vendors' logos beside accounts. They belong to their owners and are used under the owners' published branding guidelines, to identify the service an account belongs to. They are not covered by this repository's licence.

| Mark | Owner | Used for | Guidelines | Files |
|---|---|---|---|---|
| Microsoft logo | Microsoft Corporation | Microsoft accounts (both sign-in kinds) in the accounts list and the `+` menu | Sign in with Microsoft branding guidelines | `src/TimeTug.App/Assets/Providers/microsoft.svg` |
| Google "G" | Google LLC | Google accounts in the accounts list and the `+` menu | Sign in with Google branding guidelines | `src/TimeTug.App/Assets/Providers/google.svg` |

Sources, dates and the rules we follow are in `src/TimeTug.App/Assets/Providers/README.md`. The Mac app draws its own marks.

## Rust crates in the engine

The engine (`TimeTug.Engine` 2.x, the Rust library `TimeTugEngineC.dll`) is built from the crates below. The package ships the full licence texts in `THIRD-PARTY-NOTICES.md` (NuGet content file `contentFiles/any/any/include/THIRD-PARTY-NOTICES.md` of the `TimeTug.Engine` package); this table is that file's list of crates, version and licence expression, for package `2.0.0-alpha.1`. The engine carries no Swift runtime and no Foundation libraries. `calendar-*` crates and this table's engine are under the `calendar-connectors` and `timetug-shared` licences (MIT). Regenerate this table from the package's file when the pinned version changes.

| Crate | Version | Licence |
|---|---|---|
| async-trait | 0.1.92 | MIT OR Apache-2.0 |
| base64 | 0.23.1 | MIT OR Apache-2.0 |
| block-buffer | 0.12.1 | MIT OR Apache-2.0 |
| calendar-caldav | 0.0.0 | MIT |
| calendar-core | 0.0.0 | MIT |
| calendar-google | 0.0.0 | MIT |
| calendar-ical | 0.0.0 | MIT |
| calendar-ical-subscription | 0.0.0 | MIT |
| calendar-microsoft | 0.0.0 | MIT |
| calendar-oauth | 0.0.0 | MIT |
| cfg-if | 1.0.5 | MIT OR Apache-2.0 |
| cpufeatures | 0.3.1 | MIT OR Apache-2.0 |
| crypto-common | 0.2.2 | MIT OR Apache-2.0 |
| digest | 0.11.3 | MIT OR Apache-2.0 |
| displaydoc | 0.2.7 | MIT OR Apache-2.0 |
| form_urlencoded | 1.2.2 | MIT OR Apache-2.0 |
| getrandom | 0.4.3 | MIT OR Apache-2.0 |
| hybrid-array | 0.4.15 | MIT OR Apache-2.0 |
| icu_collections | 2.4.0 | Unicode-3.0 |
| icu_locale_core | 2.4.0 | Unicode-3.0 |
| icu_normalizer | 2.4.0 | Unicode-3.0 |
| icu_normalizer_data | 2.4.0 | Unicode-3.0 |
| icu_properties | 2.4.0 | Unicode-3.0 |
| icu_properties_data | 2.4.0 | Unicode-3.0 |
| icu_provider | 2.4.0 | Unicode-3.0 |
| idna | 1.1.0 | MIT OR Apache-2.0 |
| idna_adapter | 1.2.2 | Apache-2.0 OR MIT |
| itoa | 1.0.19 | MIT OR Apache-2.0 |
| jiff | 0.2.42 | Unlicense OR MIT |
| jiff-core | 0.1.4 | Unlicense OR MIT |
| jiff-static | 0.2.42 | Unlicense OR MIT |
| jiff-tzdb | 0.1.9 | Unlicense OR MIT |
| jiff-tzdb-platform | 0.1.3 | Unlicense OR MIT |
| libc | 0.2.191 | MIT OR Apache-2.0 |
| litemap | 0.8.3 | Unicode-3.0 |
| memchr | 2.8.3 | Unlicense OR MIT |
| percent-encoding | 2.3.2 | MIT OR Apache-2.0 |
| pin-project-lite | 0.2.17 | Apache-2.0 OR MIT |
| portable-atomic | 1.15.0 | Apache-2.0 OR MIT |
| portable-atomic-util | 0.2.8 | Apache-2.0 OR MIT |
| potential_utf | 0.1.6 | Unicode-3.0 |
| proc-macro2 | 1.0.107 | MIT OR Apache-2.0 |
| quick-xml | 0.42.0 | MIT |
| quote | 1.0.47 | MIT OR Apache-2.0 |
| r-efi | 6.0.0 | MIT OR Apache-2.0 OR LGPL-2.1-or-later |
| serde | 1.0.229 | MIT OR Apache-2.0 |
| serde_core | 1.0.229 | MIT OR Apache-2.0 |
| serde_derive | 1.0.229 | MIT OR Apache-2.0 |
| serde_json | 1.0.154 | MIT OR Apache-2.0 |
| sha2 | 0.11.1 | MIT OR Apache-2.0 |
| smallvec | 1.16.5 | MIT OR Apache-2.0 |
| stable_deref_trait | 1.2.1 | MIT OR Apache-2.0 |
| syn | 2.0.119 | MIT OR Apache-2.0 |
| syn | 3.0.10 | MIT OR Apache-2.0 |
| synstructure | 0.14.0 | MIT |
| thiserror | 2.0.21 | MIT OR Apache-2.0 |
| thiserror-impl | 2.0.21 | MIT OR Apache-2.0 |
| tinystr | 0.8.5 | Unicode-3.0 |
| tokio | 1.54.1 | MIT |
| tokio-macros | 2.7.2 | MIT |
| typenum | 1.20.1 | MIT OR Apache-2.0 |
| unicode-ident | 1.0.27 | (MIT OR Apache-2.0) AND Unicode-3.0 |
| url | 2.5.8 | MIT OR Apache-2.0 |
| utf8_iter | 1.1.0 | Apache-2.0 OR MIT |
| utf_types | 1.0.0 | Apache-2.0 OR MIT |
| uuid | 1.29.2 | Apache-2.0 OR MIT |
| windows-link | 0.2.1 | MIT OR Apache-2.0 |
| writeable | 0.6.4 | Unicode-3.0 |
| yoke | 0.8.4 | Unicode-3.0 |
| yoke-derive | 0.8.4 | Unicode-3.0 |
| zerofrom | 0.1.8 | Unicode-3.0 |
| zerofrom-derive | 0.1.8 | Unicode-3.0 |
| zerotrie | 0.2.5 | Unicode-3.0 |
| zerovec | 0.11.9 | Unicode-3.0 |
| zerovec-derive | 0.11.7 | Unicode-3.0 |
| zmij | 1.0.23 | MIT |
