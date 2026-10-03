# Demo covers and icons

`make demo` runs the plugin against Macro Deck with made-up sessions instead of the real ones, for store
screenshots. The tracks and apps are listed at the top of
[DemoMediaPlatform.cs](../src/SystemMedia/Demo/DemoMediaPlatform.cs).

With `make demo` running, `make screenshots` opens Macro Deck's web client in a headless Chrome and steps through
every scene in DemoMediaPlatform.cs and saves every Music Player tile on the current deck page, in dark mode,
to `artifacts/screenshots/` as `<scene>-music-player-<n>-<size>.png`, at the same size on every run. It needs Python with Playwright (`pip install playwright`) and Chrome. The first run opens a
Chrome window to sign in; the session is kept in `artifacts/web-client-profile`.

Put the images it names here (PNG or JPEG): covers such as `afterglow-avenue.png` and app icons such as
`spotify.png`. A missing cover is drawn as a gradient; a missing icon is left out.
