# S-FG: bringing TimeTug forward when a browser sign-in returns (2026-11-02)

Spike S-FG of the shared accounts list (`docs/superpowers/specs/2026-11-02-shared-accounts-list-design.md` in `binary-companion/timetug`, "Bringing TimeTug forward"). It decides only open question Q7 (whether a `timetug:` protocol link is needed); the design stands either way.

This document contains no email, account id or token. Browser versions are not needed; the browser name is enough.

## The question

When Google redirects back to TimeTug's loopback address, TimeTug (slice W1) restores the window the person last used if it is minimized and then makes the ordinary activate request (`SetForegroundWindow`), with no simulated input and no thread-input attachment. Windows may refuse that request when TimeTug does not hold the foreground right, and then it flashes TimeTug's taskbar button instead. How often does each happen?

## What the agent has done

- The request, the restore, the refusal path and the "no open window" case are covered by unit tests over a fake window API (`SignInForegroundRequesterTests`).
- The listener raises the request once per captured redirect, for a code and for an error, and never for the favicon, a stray request or a cancelled wait (`LoopbackAuthorizationTests`).
- Not measured: what Windows does with the real request. It depends on the default browser, on which window had the foreground when the redirect arrived, and on Windows' foreground-lock rules, so it needs a person at a screen.

## What the owner runs (checklist 2.17)

Use the `msix-arm64` (VM) or `msix-x64` artifact of the pull request or of `main`, a Google sign-in, and each combination below. Do not touch TimeTug after the browser opens until the redirect has happened. Write what happened in the table: **forward** (TimeTug is in front and has the keyboard), **flash** (the taskbar button flashes orange, TimeTug stays behind), or **nothing** (no visible effect).

| Default browser | TimeTug in front when the browser opens | TimeTug behind other windows | TimeTug minimized |
| --- | --- | --- | --- |
| Edge, VM | not yet run | not yet run | not yet run |
| Chrome, VM | not yet run | not yet run | not yet run |
| Firefox, VM | not yet run | not yet run | not yet run |
| Edge, other PC | not yet run | not yet run | not yet run |
| Chrome, other PC | not yet run | not yet run | not yet run |
| Firefox, other PC | not yet run | not yet run | not yet run |

## Decision (Q7)

Default, used until the owner's results say otherwise: accept the request as it is. If the table shows Windows refusing most of the time, the alternative is an "Open TimeTug" link on the landing page that activates the packaged app through a registered `timetug:` protocol (a person's click is allowed to bring an app forward); it needs a manifest protocol declaration and a Store capability review and is not part of this work.
