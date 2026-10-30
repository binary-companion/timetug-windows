# Package capabilities and extensions: why each is declared

Partner Center asks for a justification of each restricted capability, and certification reviewers read the manifest. These are the declarations in `src/TimeTug.App/Package.Store.appxmanifest` (the same set as the development package; an architecture test fails if a release manifest declares more).

## Capabilities

| Declared | Why |
| --- | --- |
| `runFullTrust` (restricted) | TimeTug is a WinUI 3 desktop app packaged as MSIX, not a UWP app. It runs the portable engine as native libraries inside its own process (the Swift runtime and the engine DLLs), shows borderless always-on-top windows on every display for the takeover, owns a notification-area icon and a global shortcut, and listens on the loopback address (127.0.0.1) for the browser sign-in redirect. None of that is available inside an AppContainer. It never runs elevated and asks for no other privilege. |
| `internetClient` | Outgoing connections to the calendar providers the person connects: Google Calendar and Microsoft Graph (HTTPS), iCloud and other CalDAV servers. |

Not declared, on purpose: any file-system, document, picture, contacts, location, microphone, camera or device capability; `internetClientServer` and `privateNetworkClientServer` (the loopback listener is the app's own redirect target and is closed after sign-in); `broadFileSystemAccess`; `allowElevation`.

## Extensions in the manifest

| Extension | Why |
| --- | --- |
| `windows.startupTask` (`TimeTugStartup`, disabled) | "Start TimeTug when I sign in". Off until the person turns it on in the welcome window or Settings; Windows shows it in Settings > Apps > Startup. |
| `windows.toastNotificationActivation` and the `windows.comServer` class behind it | The buttons on the toast the app shows when it cannot take over the screen (for example during a full-screen app). Windows starts or signals the app through this class. |

There is no execution alias in the Store and Direct manifests (the development alias exists only in the development package).

## Other things a reviewer may ask

- **Does it run in the background?** Yes, as the tray app the person starts or sets to start at sign-in. It does nothing without a connected calendar except wait. It makes no network call when no account is connected.
- **Does it collect data?** No telemetry and no TimeTug servers. Diagnostics are a local log file that "Copy diagnostics" lets the person share; event titles, links and calendar names are never written to it.
- **Sign-in:** Google and Microsoft sign in through the default browser. The OAuth client ids of the shipped build are those of Binary Companion's registrations; they are not secret and are not printed anywhere.
