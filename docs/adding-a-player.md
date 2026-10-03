# Adding a player

System Media shows every app that reports to the system's media controls: the Windows media overlay,
Now Playing on macOS or MPRIS on Linux. A player that doesn't (SMPlayer and plain mpv, for example) is
added to the plugin itself as one small C# class. Once it ships, it works for everyone with no setup.

If you'd rather not write code, [request the player](https://github.com/PyFlat/System-Media/issues/new?template=player_request.yml) instead. The form asks a few plain
questions, and that is usually enough for someone to add it.

## The class

A player answers three questions: what is it called, what is playing right now, and what can it do. Here
is a complete player for an imaginary "Foo Player" with a web interface on port 8880:

```csharp
using System.Net.Http.Json;
using MacroDeck.Sdk.MusicPlayer;

namespace SystemMedia.Players;

internal sealed class FooPlayer : Player
{
	private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

	// Becomes the app id "player:foo-player". Never change it after release: widgets store it.
	internal override string Id => "foo-player";

	internal override string Name => "Foo Player";

	// Lets the plugin show the app's icon and, on Windows, its volume mixer level.
	internal override string? ProcessName => "fooplayer";

	// Called every second. Return null while the player isn't running.
	internal override async Task<PlayerReading?> ReadAsync(CancellationToken cancellationToken)
	{
		try
		{
			var status = await _http.GetFromJsonAsync<Status>("http://localhost:8880/api/status", cancellationToken);
			if (status is null)
			{
				return null;
			}

			return new PlayerReading(
				status.Playing ? PlaybackState.Playing : PlaybackState.Paused,
				status.Title,
				status.Artist,
				Position: TimeSpan.FromSeconds(status.Position),
				Duration: TimeSpan.FromSeconds(status.Length));
		}
		catch (HttpRequestException)
		{
			return null;
		}
	}

	internal override bool Supports(PlayerCommand command) => command is PlayerCommand.Toggle or PlayerCommand.Next;

	internal override async Task<bool> SendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken)
	{
		var path = command == PlayerCommand.Next ? "next" : "playpause";
		using var response = await _http.PostAsync($"http://localhost:8880/api/{path}", content: null, cancellationToken);
		return response.IsSuccessStatusCode;
	}

	private sealed record Status(bool Playing, string? Title, string? Artist, double Position, double Length);
}
```

Then add it to the list in [`PlayersSource.cs`](../src/SystemMedia/Players/PlayersSource.cs):

```csharp
: this([new MpvPlayer(), new SmPlayer(), new FooPlayer()], logger, time)
```

That's all. The widget, the variables, the events and the "Any app" instance pick it up from there.

## What you can fill in

| Member | Required | What it does |
|---|---|---|
| `Id` | yes | Lowercase letters, digits and dashes. Never renamed once released. |
| `Name` | yes | Shown in Macro Deck as **System Media - &lt;Name&gt;**. |
| `ReadAsync` | yes | What is playing: state, title, artist, album, position, duration, volume (0 to 100), and `HasCover: true` if `ReadCoverAsync` can return a picture. |
| `ProcessName` | no | The process name without `.exe`, for the icon and the Windows volume mixer. |
| `Interval` | no | How often `ReadAsync` runs. One second by default. |
| `ReadCoverAsync` | no | The current cover as image bytes. Asked once per track. |
| `Supports` / `SendAsync` | no | The commands: `Play`, `Pause`, `Toggle`, `Next`, `Previous`, `Seek` (`value` is seconds) and `Volume` (`value` is 0 to 100). |

You don't need every command. `Toggle` alone also serves play and pause, and `Play` plus `Pause` also serve
toggle. Return `false` from `SendAsync` when the player refuses, so the button reports a failure instead of
pretending it worked. A volume is only shown when `Supports(PlayerCommand.Volume)` is true.

[`MpvPlayer.cs`](../src/SystemMedia/Players/MpvPlayer.cs) and
[`SmPlayer.cs`](../src/SystemMedia/Players/SmPlayer.cs) are real examples: SMPlayer reuses everything from
mpv and only changes next, previous and volume.

## Rules

- **Only talk to what the user installed.** Never bundle or download another program.
- **Never run a shell.** If you must start a program, start it directly with `ProcessStartInfo.ArgumentList`
  and `UseShellExecute = false`, so text from the player can never become a command.
- **Return quickly.** Use a short timeout and pass the `cancellationToken` on.
- **Return null, don't throw,** when the player simply isn't running.

## Trying it

```bash
make build && make test
make run      # runs the plugin against your Macro Deck
```

Start the player, add a Music Player widget and pick **System Media - Foo Player**. When it works, open a
pull request.
