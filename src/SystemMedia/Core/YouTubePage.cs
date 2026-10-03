using System.Text.RegularExpressions;

namespace SystemMedia.Core;

internal sealed partial record YouTubePage(string VideoTitle, string? VideoId)
{
	internal static YouTubePage? FromTab(BrowserTab tab) =>
		VideoTitleOf(tab.Title) is { } title ? new YouTubePage(title, VideoIdOf(tab.Url)) : null;

	// "(3) Video - YouTube", plus whatever the browser appends to a tab or window title.
	internal static string? VideoTitleOf(string title)
	{
		var match = VideoPageTitle().Match(title);
		return match.Success && match.Groups["title"].Value.Trim() is { Length: > 0 } video ? video : null;
	}

	// Firefox shows addresses without their scheme.
	internal static string? VideoIdOf(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}

		var text = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
		if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
		{
			return null;
		}

		var host = uri.Host.ToLowerInvariant();
		var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		string? id = null;
		if (host == "youtu.be")
		{
			id = segments.FirstOrDefault();
		}
		else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
		{
			id = segments switch
			{
				["watch"] => QueryValue(uri.Query, "v"),
				["shorts" or "live", var value, ..] => value,
				_ => null,
			};
		}

		return id is not null && VideoIdPattern().IsMatch(id) ? id : null;
	}

	private static string? QueryValue(string query, string name)
	{
		foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = pair.IndexOf('=', StringComparison.Ordinal);
			if (separator > 0 && string.Equals(pair[..separator], name, StringComparison.Ordinal))
			{
				return Uri.UnescapeDataString(pair[(separator + 1)..]);
			}
		}

		return null;
	}

	[GeneratedRegex(@"^(?:\(\d+\+?\)\s*)?(?<title>.+) - YouTube(?:$|\s+[-–—])")]
	private static partial Regex VideoPageTitle();

	[GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
	private static partial Regex VideoIdPattern();
}
