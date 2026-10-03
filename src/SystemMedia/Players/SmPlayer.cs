namespace SystemMedia.Players;

// SMPlayer drives mpv over a pipe named after SMPlayer's process id; reading it gives the full state and the
// real cover. Next and previous belong to SMPlayer's own playlist, so they go to SMPlayer the way
// "smplayer -send-action" does, which also works for the Microsoft Store version.
internal sealed class SmPlayer() : MpvPlayer(["smplayer-mpv-*"])
{
	private const string Socket = "qtsingleapp-smplay-*";

	internal override string Id => "smplayer";

	internal override string Name => "SMPlayer";

	internal override string? ProcessName => "smplayer";

	// SMPlayer starts mpv again for every file with its own volume, so a volume set here would not last.
	internal override bool Supports(PlayerCommand command) => command != PlayerCommand.Volume;

	internal override Task<bool> SendAsync(PlayerCommand command, double? value, CancellationToken cancellationToken) =>
		command switch
		{
			PlayerCommand.Next => QtSingleApplication.SendAsync(Socket, "action play_next", cancellationToken),
			PlayerCommand.Previous => QtSingleApplication.SendAsync(Socket, "action play_prev", cancellationToken),
			PlayerCommand.Volume => Task.FromResult(false),
			_ => base.SendAsync(command, value, cancellationToken),
		};
}
