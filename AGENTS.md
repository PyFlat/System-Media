# Agent guidance

**System Media** is a Macro Deck 3 out-of-process plugin (Windows, macOS and Linux) that exposes the operating
system's media sessions as a Music Player provider. Players the system does not see (SMPlayer, mpv) are
built in as `Player` classes. [README.md](README.md) is the user-facing guide; this file is the rule set
for changing the code. Keep it current when a rule stops matching reality.

## Orientation

```
src/SystemMedia/
  Program.cs                builder chain
  PluginIntegration.cs      instances (current + one per known app, per-widget cycling), variables, events,
                            issues, the optional settings page
  SettingsConfigFlow.cs     the settings page (exact YouTube info)
  ShowNextPlayingAppAction  switches "Any app" to the next playing app
  SetDefaultAudioDevice...  switches a default sound device (also done by writing a device variable)
  Core/                     platform-neutral: the player, snapshot, state mapping, leftover filter and
                            thumbnail history, "any app" picker, instance ids, remembered apps, settings,
                            YouTube correction
  Events/                   event definitions, change detection, the once-a-second watcher
  Variables/                deck variables for the current session and the default sound devices
  Platform/
    IMediaPlatform.cs       the interface every OS backend implements
    IAudioDevices.cs        the default sound devices, read and switched per OS
    CompositeMediaPlatform  the OS backend plus every IMediaSource, routed by app id
    Windows/                GSMTC sessions, volume mixer and sound devices (Audio/), app names (Shell/), icons, VLC add-on,
                            browser tabs (Browser/)
    Linux/                  MPRIS over the D-Bus session bus (Tmds.DBus.Protocol), app icons from .desktop
                            files and the hicolor theme, sound devices through pactl
    MacOS/                  Now Playing through the user's Homebrew media-control, app names/icons via
                            mdfind/sips, sound devices through Core Audio
  Players/                  players the system does not see: the Player base class, MpvPlayer, SmPlayer,
                            the watcher that polls each, local sockets and the Qt single-app message
docs/                       adding-a-player.md, the contributor guide for a new Player
tools/StoreCanvas.cs        pads the store screenshots onto a 16:9 canvas (make screenshots STORE=1)
tests/SystemMedia.Tests/    shared tests, plus Windows/ (Windows build only)
```

SDK behaviour is documented upstream in the
[Macro Deck 3 repository](https://github.com/Macro-Deck-App/Macro-Deck-3/tree/main/docs/plugin-development).
Look there instead of guessing.

## Rules specific to this plugin

**Public API.** These are persisted in users' widgets, templates and automations. Renaming one breaks them:

- instance ids (`InstanceIds.ForApp`, `InstanceIds.Current`; a test pins the exact output)
- event ids, event parameter names and variable names (`system_media_*`)
- player app ids, `player:<Player.Id>` (`player:mpv`, `player:smplayer`)
- the data files `known-apps.json` (its `appUserModelId` field is the 1.x name), `settings.json` and
  `thumbnail-history.json`
- the "Any app" widget option names (`cycle`, `cycle-seconds`) and settings field names
  (`exact_youtube_info`), stored in users' widgets and config entry
- action ids, parameter names and choice values (`set-default-audio-device` with `role` and `device`),
  and the device ids it stores: the Windows endpoint id, the macOS device UID, the PulseAudio sink or
  source name

**Instances.** The host only resolves instance ids `GetInstances()` currently lists and uses the first as
the default. `current` stays first, and an app once seen stays listed (and remembered on disk). Names carry
the `System Media - ` prefix because the host's picker mixes every provider's instances.
The settings flow must keep `AllowsMultipleConfigurations` true: the host renames every instance of a
single-configuration integration to `default` and keeps only the first.

**Platforms.**

- One target framework per build (`Directory.Build.props`). `Platform/Windows/**` only compiles in the
  Windows build and `Platform/Linux/**` (with its D-Bus package) only outside it; `tests/.../Linux` runs
  with `-p:SystemMediaWindows=false`. Keep shared code free of Windows types and check it that way.
- New platform behaviour goes behind `IMediaPlatform`, never into `Core/` behind an OS check. A platform
  returns `null`/`false` for an app with no session and throws only for a platform-level failure.
- Non-OS apps come from an `IMediaSource` (only `PlayersSource` today), whose ids must never collide
  with the OS's.
- macOS reports one Now Playing app and has no per-app volume. Do not emulate either.
- Linux has no system-wide current player; the one that started playing last stands in. MPRIS volume is
  the player's own, so a player without a `Volume` property gets no slider. An app id is the bus name
  after `org.mpris.MediaPlayer2.` without a `.instance...` suffix, so every instance is one app.
- Sound devices are an `IAudioDevices` behind `IMediaPlatform.AudioDevices`: Core Audio on Windows and
  macOS, `pactl --format=json` on Linux (it ships with PulseAudio and `pipewire-pulse`, run with
  `LC_NUMERIC=C` because some locales break its JSON). Only Windows has communication defaults; elsewhere
  those roles are the plain defaults. Windows switches through the undocumented `IPolicyConfig`, as every
  device switcher does; keep it confined to `WindowsAudioDevices`.
- No third-party program is bundled or run unless the user installed it (Store guideline 2); managed NuGet
  libraries such as Tmds.DBus.Protocol are fine. macOS relies on the user installing `media-control`
  from Homebrew; its `stream --micros` output is what `NowPlayingState` parses. Its version is not pinned,
  so CI checks the current one on every run.

**Windows.**

- Volume is the app's volume mixer level via Core Audio; the media transport controls have none.
- Await every WinRT operation through `WinRtAwait.OnThreadPool` and never touch a WinRT object after an
  await, or later calls fail with `RPC_E_WRONG_THREAD`.
- COM interfaces (`CoreAudioNative`, `ShellNative`, `UiAutomationNative`) bind by vtable position: declare
  every method up to the last one called, in header order, and release every RCW.
- Browser tabs are read through UI Automation COM, relying on each browser's internal UI names in
  `BrowserUi` (Firefox `tabbrowser-tab`/`main-button`/`urlbar-input`, Chromium `Tab` or `EdgeTab`/
  `AlertIndicatorButton`/`OmniboxViewViews`), never on localized labels. Check them when a browser update
  breaks it. Chrome appends localized status to tab names, so page titles are matched by prefix.

**Behaviour.**

- A command the app declines throws, so the SDK's actions report failure. Never turn it into a success.
- The first observation of an app is a baseline, never an event.
- A platform that cannot start (no media service, no D-Bus session, no `media-control`) is an issue for
  the user and a Warning in the log, never an Error: initialization runs again after every reconnect, and
  conformance check MDC0603 fails on any Error it logs. A GitHub runner has none of these, so a release's
  stub-host run always takes this path.
- A platform reports itself unavailable only after a start attempt failed, not while the first start runs.
- `SessionMusicPlayer` applies the YouTube correction after the leftover filter and timeline restorer, so
  the filter always compares what the app itself reported.
- `DroppedTimelineRestorer` keeps a timeline across play/pause only, never after a seek.
- Invasive fixes are opt-in. The exact YouTube info makes browsers enable accessibility and contacts
  YouTube, so while off no tab is read and nothing is sent. It is switched on the settings page, a config flow
  with `RequiresConfiguration = false` so the integration runs without ever saving it; an issue without a
  button points there while a YouTube video is open. The VLC
  issue only writes VLC's `vlcrc`, only on the user's button press. Never install anything unasked.

**Untrusted input.** A plugin was once rejected for a PowerShell call that could be injected. Keep it impossible:

- Start every process with `UseShellExecute = false` and `ArgumentList`: never a shell, never a joined
  command line. A value from an app, a web page or a player's output only ever becomes one argument. The one
  `UseShellExecute = true` opens the constant VLC download URL.
- Values other apps control (MPRIS properties, tab URLs and titles, mpv's replies) are checked before they
  reach a URL, a query or a path: the YouTube id against its pattern, a macOS bundle id before `mdfind`, a
  `DesktopEntry` as a plain file name. Artwork is passed on only when its bytes are an image.

**Demo mode.** `Demo/` (made-up sessions for `make demo`) only compiles in Debug builds and is switched on
by `SYSTEM_MEDIA_DEMO`. Never reference it from code that builds in Release, and never let tests depend
on it: `make test` runs Release.

**Players.**

- A new player is a `Player` subclass added to the list in `PlayersSource`. Keep
  `docs/adding-a-player.md` and its example compiling with the base class.
- There are no user-defined players: JSON definitions were removed as too hard to set up and too
  machine-specific. Support a player by adding it here.
- `MpvPlayer` only connects to a pipe seen on the previous poll (SMPlayer must win the race to its own
  mpv), and a player that stops answering stays present for five seconds (SMPlayer restarts mpv per file).

## SDK rules

- Identity lives in `manifest.json` only. Ids in source are local ids (`^[a-z][a-z0-9]*(-[a-z0-9]+)*$`).
- Keep `Program.cs` as the plain builder chain. Constructors must be side-effect free: `Build()` constructs
  everything to validate it. Never set a listener URL. Only write to `MACRO_DECK_PLUGIN_DATA_DIRECTORY`.
- `InitializeAsync` runs again after every reconnect and config change, so it must be idempotent.
- `ActionResult` must be truthful: `Failed(code, message)` for anything that did not happen. Forward
  `context.CancellationToken`. No blocking waits and no `async void` in SDK contract types.
- Call `IPluginCatalogNotifier.CatalogChanged` whenever instances, variables or issues change outside a host
  invocation.
- Every user-facing string is a dotted key in `Localization/Strings.resx`; check `MacroDeckStrings` first.
  Log and exception messages stay English literals.
- Log through Serilog. Structured properties are not persisted to the log file, so put what matters into
  the message template.

## Code style

- Build warning-free. Do not relax `Directory.Build.props`. Suppress a diagnostic narrowly and with a reason.
- C# in `src/` is tab-indented.
- Prefer no comment. Write a short `//` line only for what the code cannot say: a race, a workaround, a
  protocol or OS quirk. No XML doc summaries, no banners. If a name needs a comment, rename it.
- No em dashes in code, comments, commits or docs.

## Verifying a change

```bash
make build && make test
dotnet test SystemMedia.slnx -p:SystemMediaWindows=false  # the non-Windows variant, incl. the Linux tests
make conformance                                          # after changing capability shape, cancellation or the manifest
```

Some tests run against the machine's real media sessions and only assert what holds whether or not
something plays. To see real apps, use `make run` while a browser or Spotify plays.

## Workflow

- Work on a branch (`feature/`, `fix/`, `refactor/`, `chore/`, `docs/`, `ci/`), keep changes focused.
- Do not push or open a pull request unless asked. No AI attribution or co-author trailers.
- A release is a pushed `vX.Y.Z` tag matching `manifest.json`'s `version` (`make release`).
- Update README.md when the build, run, packaging or user-facing behaviour changes. Its Privacy section
  must list every network call and every file the plugin stores (Store guideline 8).
