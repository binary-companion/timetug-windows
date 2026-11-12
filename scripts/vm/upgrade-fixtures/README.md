# Synthetic engine data for the upgrade check

These four files are the engine's data folder as an older build left it, with no real account and no calendar content (sample names such as `dave@contoso.com` and `sample meeting a`). `scripts/vm/vm-upgrade-check.ps1` copies them into the development package's `LocalState` under the names the engine uses on disk.

| File here | Source | Origin name |
|---|---|---|
| `accounts.json` | `calendar-connectors` `fixtures/persisted-formats/1.2.0/connections.json` (one `microsoft-brokered` account) | `connections.json` |
| `sync-state.json` | `calendar-connectors` `fixtures/persisted-formats/1.2.0/sync-state.json` | same |
| `dedup-state.json` | `timetug-shared` `fixtures/engine/dedup-state.json` (one lesson, one verdict) | same |
| `takeover-ledger.json` | `timetug-shared` `fixtures/engine/takeover-ledger.json` (four records) | same |

The names are the ones `timetug-shared` `v2` uses (`crates/timetug-engine-protocol/src/tests/data/legacy-data-dir/` holds the same set). The app's own settings file (`settings.json`) is not here: the check creates it by running the older build once and changing three settings through the `upgrade-seed` self-test scenario.
