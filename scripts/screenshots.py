"""Store screenshots of the real Music Player widget, taken from Macro Deck's web client.

Run `make demo` in one terminal, then `make screenshots` in another. It switches the demo through every scene
in DemoMediaPlatform.cs and saves each Music Player tile per scene, in dark mode. The first run opens a Chrome
window to sign in to the web client; the session is kept in artifacts/web-client-profile for later runs.
"""

import asyncio
import os
import shutil
import sys
from pathlib import Path

from playwright.async_api import TimeoutError as PlaywrightTimeout, async_playwright

ROOT = Path(__file__).resolve().parent.parent
PROFILE = ROOT / "artifacts" / "web-client-profile"
OUTPUT = ROOT / "artifacts" / "screenshots"
DEMO = ROOT / "demo"

# The host pushes a changed state within about a second; covers load right after.
SCENE_SETTLE_MS = 3000
CYCLE_TIMEOUT_MS = 60000


def host_url() -> str:
    port_file = Path(os.environ["APPDATA"] if os.name == "nt" else Path.home() / ".config") / "app.macro-deck.macrodeck" / "loopback-port"
    port = port_file.read_text().strip() if port_file.exists() else "29919"
    return f"http://127.0.0.1:{port}/"


async def signed_in(page) -> bool:
    return await page.get_by_role("button", name="Sign in").count() == 0


async def open_deck(playwright, url: str, headless: bool):
    context = await playwright.chromium.launch_persistent_context(
        str(PROFILE),
        channel="chrome",
        headless=headless,
        viewport={"width": 1280, "height": 800},
        device_scale_factor=2,
        color_scheme="dark",
    )
    # The web client keeps its theme in local storage and otherwise follows the system's.
    await context.add_init_script("localStorage.setItem('md.appearance.themeMode', 'dark')")
    page = context.pages[0] if context.pages else await context.new_page()
    await page.goto(url, wait_until="networkidle")
    await page.wait_for_timeout(1500)
    return context, page


async def sign_in(playwright, url: str) -> None:
    context, page = await open_deck(playwright, url, headless=False)
    print("Sign in to Macro Deck in the Chrome window that just opened (waiting up to 5 minutes)...")
    for _ in range(300):
        if await signed_in(page):
            break
        await page.wait_for_timeout(1000)
    else:
        await context.close()
        sys.exit("Not signed in; run make screenshots again.")
    await page.wait_for_timeout(3000)
    await context.close()
    print("Signed in. The session is kept for later runs.")


# Waits until every "Any app" tile names the scene's app; a cycling widget can take a round to get there.
SHOWS_APP = """shown => [...document.querySelectorAll('.deck-grid-tile')]
    .filter(tile => tile.querySelector('[data-node-id$=".labels.providerName"]')?.textContent.trim() === 'System Media - Any app')
    .every(tile => tile.querySelector('[data-node-id$=".labels.source"]')?.textContent.trim() === shown)"""


async def settle(page, shown: str) -> None:
    await page.wait_for_timeout(SCENE_SETTLE_MS)
    if shown:
        try:
            await page.wait_for_function(SHOWS_APP, arg=shown, timeout=CYCLE_TIMEOUT_MS)
        except PlaywrightTimeout:
            print(f"    'Any app' did not show {shown} within {CYCLE_TIMEOUT_MS // 1000} s; capturing anyway.")
        await page.wait_for_timeout(1000)
    await page.wait_for_function("[...document.images].every(image => image.complete)")


async def capture_scene(page, scene: str) -> int:
    players = page.locator(".deck-grid-tile").filter(has=page.locator('[data-node-id="musicPlayer"]'))
    count = await players.count()
    for index in range(count):
        tile = players.nth(index)
        box = await tile.bounding_box()
        name = f"{scene}-music-player-{index + 1}-{round(box['width'])}x{round(box['height'])}.png"
        await tile.screenshot(path=str(OUTPUT / name), omit_background=True)
    return count


async def capture(playwright, url: str) -> None:
    scenes = DEMO / ".scenes"
    if not scenes.exists():
        sys.exit("No demo scenes found: start make demo first (and keep it running).")
    entries = [line.split("	") + [""] for line in scenes.read_text(encoding="utf-8").splitlines() if line.strip()]
    names = [(entry[0], entry[1]) for entry in entries]

    context, page = await open_deck(playwright, url, headless=True)
    await page.wait_for_selector(".deck-grid-tile", timeout=15000)
    await page.add_style_tag(content="html, body { background: transparent !important; }")

    shutil.rmtree(OUTPUT, ignore_errors=True)
    OUTPUT.mkdir(parents=True)
    try:
        for name, shown in names:
            (DEMO / ".scene").write_text(name, encoding="utf-8")
            await settle(page, shown)
            count = await capture_scene(page, name)
            print(f"  {name}: {count} Music Player tile(s)")
    finally:
        (DEMO / ".scene").unlink(missing_ok=True)
        await context.close()

    print(f"Wrote the tiles of {len(names)} scenes to {OUTPUT}")


async def main() -> None:
    url = host_url()
    async with async_playwright() as playwright:
        context, page = await open_deck(playwright, url, headless=True)
        needs_sign_in = not await signed_in(page)
        await context.close()
        if needs_sign_in:
            await sign_in(playwright, url)
        await capture(playwright, url)


asyncio.run(main())
