# System Media

A [Macro Deck 3](https://macro-deck.app/) plugin that connects Macro Deck's **Music Player** widget to
whatever is playing on your computer. It reads the operating system's own media controls (the Windows
media overlay, macOS Now Playing, MPRIS on Linux), so any app that shows up there works without setup:
Spotify, browsers (YouTube, SoundCloud, Netflix, ...), Apple Music, TIDAL, foobar2000, MusicBee and more.
**SMPlayer** and **mpv**, which the system does not see, are
[supported directly](#players-the-system-does-not-see).

|                                              | Windows                                         | macOS                                | Linux                                    |
| -------------------------------------------- | ----------------------------------------------- | ------------------------------------ | ---------------------------------------- |
| Apps                                         | every app in the media overlay, several at once | the one app Now Playing shows        | every MPRIS player, several at once      |
| Volume                                       | the app's own volume mixer level                | hidden (macOS has no per-app volume) | the player's own volume, where it has one |
| Controls, cover, progress, variables, events | yes                                             | yes                                  | yes                                      |

On macOS the plugin needs [media-control](https://github.com/ungive/media-control), because since
macOS 15.4 only Apple's own programs may read Now Playing. Install it with [Homebrew](https://brew.sh):

```sh
brew install media-control
```

Until it is installed, the integration shows an issue with this command and a **Check again** button.

On Linux the plugin reads the players that publish themselves over MPRIS on the D-Bus session bus, which
Spotify, Firefox, Chromium-based browsers, VLC, Rhythmbox, Elisa and most others do (mpv needs the
`mpv-mpris` plugin). Nothing has to be installed. Macro Deck has to run inside your desktop session to
reach the session bus; otherwise the integration shows an issue with a **Try again** button. Linux has no
system-wide "current" player, so *Any app* treats the player that started playing last as current.

## Usage

Install the plugin, add a **Music Player** widget and pick an instance:

- **System Media - Any app** follows whatever is playing, without jumping between two playing apps.
- **System Media - Spotify**, **System Media - Firefox**, ... stays on one app. An app is listed once it
  has played something and is remembered, so a widget pinned to it keeps working while the app is closed.

The widget shows title, artist, album, cover (or the app's icon when a track has none), progress and
state, and supports play/pause, next, previous, seek, shuffle, repeat and volume. The same controls are
available as actions. What works depends on what the app exposes: a browser tab often cannot skip, and
some apps report no timeline. A command the app declines is reported as a failed action.

### Several apps playing at once

By default *Any app* shows the app the system calls current while it plays, and some apps (VLC with its
add-on) stay current while others play too. You can choose instead:

- **Cycle through playing apps**, in the editor of a Music Player widget showing *Any app*, makes that
  widget show each playing app in turn for the **Seconds per app** you set (10 by default). Each widget
  has its own setting. While it cycles, the header shows the app's place among the playing ones as a
  badge, e.g. `2/3`, and the widget's buttons control the app it shows.
- The **Show the next playing app** action switches *Any app* to the next app that is playing. It stays
  there while that app plays. Put it on a button to flip through your players by hand.

The widget's header also names the app *Any app* is showing, e.g. "Firefox". The widget's **Source** option
hides the app name and the badge.

Only apps that are playing take part; a paused app is skipped.

### Variables

For whatever is playing right now (the same as *Any app*):

| Variable                                                               | Value                                        |
| ---------------------------------------------------------------------- | -------------------------------------------- |
| `system_media_app_name`                                                | The playing app, e.g. `Firefox`              |
| `system_media_track_name`, `system_media_artist`, `system_media_album` | Track metadata                               |
| `system_media_playback_state`                                          | `playing`, `paused` or `stopped`             |
| `system_media_is_playing`                                              | `true` while playing                         |
| `system_media_position`, `system_media_duration`                       | Seconds; the position can be set to seek     |
| `system_media_progress_percentage`                                     | 0 to 100                                     |
| `system_media_volume`                                                  | The app's volume in percent; can be set      |
| `system_media_shuffle_enabled`, `system_media_repeat_mode`             | `repeat_mode` is `off`, `track` or `context` |

### Events

Each event can be narrowed to one app with its **App** field and carries `app` and `appName`:

| Event                      | Fires when                                            | Payload                    |
| -------------------------- | ----------------------------------------------------- | -------------------------- |
| **Track changed**          | an app starts a different track, video or stream      | `track`, `artist`, `album` |
| **Playback state changed** | an app starts playing, pauses or stops                | `state`                    |
| **Playing app changed**    | the system switches its media controls to another app | `previousAppName`          |

Every detected change is also written to Macro Deck's log viewer.

### Browsers and YouTube

Browsers keep one media session for all tabs, so a background tab or a YouTube hover preview can take over
the widget for a moment. A page that publishes only a title (TikTok) would inherit the previous page's
cover and progress, and Firefox can even bring back a cover from a tab closed hours ago. The plugin detects
and hides those leftovers: it remembers which track each recent cover first appeared with, across restarts,
stored as hashes only (no titles or pictures).

YouTube in Firefox, Chrome and Edge often reports the title and cover of a video you only hovered over.
**Exact YouTube info** (Windows, off by default) fixes that. Switch it on in System Media's settings on its
integration page; while it is off and a browser window shows a YouTube video, the integration shows a
notice pointing there. While on, the plugin reads the browser's tabs through UI Automation (which makes the
browser enable its accessibility support, hence opt-in) and looks the real video up through YouTube's
public oEmbed endpoint. Turning off YouTube's *Inline playback* setting avoids most hover previews without any of this.

### VLC

VLC only reports to the Windows media controls with the community add-on
[vlc-win10smtc](https://github.com/spmn/vlc-win10smtc). When VLC is installed, the plugin shows what is
missing as an issue on its integration: a download link while the add-on is missing, then a **Turn on**
button that enables it in VLC's settings. By hand:

1. Download the zip matching **VLC's** architecture (a VLC under `Program Files (x86)` needs `x86`).
2. Copy `libwin10smtc_plugin.dll` into `<VLC folder>\plugins\misc`.
3. In VLC: *Tools > Preferences*, *Show settings: All*, *Interface > Control interfaces*, tick
   **Windows 10 SMTC integration**, save and restart VLC.

### Players the system does not see

Some players don't report to the system's media controls. System Media talks to these directly:

- **SMPlayer** (including the Store version) works without setup.
- **mpv** needs `input-ipc-server=mpvsocket` in its `mpv.conf` (`/tmp/mpvsocket` on macOS and Linux).
  On Linux the `mpv-mpris` plugin works too; use one or the other, or mpv is listed twice.

Missing a player? [Request it](https://github.com/PyFlat/System-Media/issues/new?template=player_request.yml): a short form, no coding needed. Developers can also add one
themselves with a single small class, see [docs/adding-a-player.md](docs/adding-a-player.md).

## Privacy

Everything runs on your computer. The plugin sends nothing about you or your media anywhere.

- **Network:** only with *Exact YouTube info* switched on, the id of the YouTube video you watch is sent
  to `www.youtube.com/oembed` and its thumbnail is fetched from `i.ytimg.com`. On Linux, a cover a player
  reports as a web address is downloaded from there (see below).
- **Stored** in the plugin's data folder: `known-apps.json` (the ids and names of apps that played media),
  `settings.json` (the Exact YouTube info choice of 1.0.x, read until you save the settings page) and
  `thumbnail-history.json` (hashes of recent covers and tracks, no titles or pictures).
- **SMPlayer and mpv** are read and controlled over their local IPC pipe or socket. A cover is a
  screenshot mpv writes to a temporary file, which is deleted right after it is read.
- **macOS:** Now Playing is read by running your installed `media-control`; app names and icons come
  from `mdfind`, `plutil` and `sips`. Nothing third-party ships with the plugin.
- **Linux:** players are read over the D-Bus session bus with the bundled
  [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) library (MIT). A player that reports its cover
  as a web address (Spotify does) has the cover downloaded from there, as the desktop's own media controls
  do. App icons come from the apps' `.desktop` files and the icon theme.

## Development

```bash
make build        # dotnet build
make test         # dotnet test
make run          # run against the installed Macro Deck (make watch for hot reload)
make stub         # run against a stub host, no Macro Deck needed
make demo         # run with made-up tracks from demo/, for screenshots
make screenshots  # while make demo runs: save the deck's Music Player tiles as PNGs
make pack         # build and inspect this platform's .macroDeckPlugin
make release     # tag and publish manifest.json's version (VERSION=x.y.z bumps it first)
```

`make` lists everything. On Windows it needs GNU make and Git Bash's `sh` on `PATH`. You can also debug with
the **Macro Deck - Real Host** launch profile; supply a first-run enrollment token only through .NET User
Secrets.

A build targets the OS it runs on (`net10.0-windows…` on Windows, `net10.0` elsewhere).
`-p:SystemMediaWindows=false` builds the non-Windows variant on Windows. Running on a Mac needs
`media-control` installed, as above.

## License

MIT.
