using Tmds.DBus.Protocol;

namespace SystemMedia.Platform.Linux;

internal sealed class MprisBus : IDisposable
{
	internal const string NamePrefix = "org.mpris.MediaPlayer2.";
	internal const string RootInterface = "org.mpris.MediaPlayer2";
	internal const string PlayerInterface = "org.mpris.MediaPlayer2.Player";

	private const string ObjectPath = "/org/mpris/MediaPlayer2";
	private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

	private readonly DBusConnection _connection;

	private MprisBus(DBusConnection connection) => _connection = connection;

	internal static async Task<MprisBus> ConnectAsync()
	{
		var address = DBusAddress.Session ?? throw new InvalidOperationException("No D-Bus session bus address is set (DBUS_SESSION_BUS_ADDRESS).");
		var connection = new DBusConnection(address);
		try
		{
			await connection.ConnectAsync();
			return new MprisBus(connection);
		}
		catch
		{
			connection.Dispose();
			throw;
		}
	}

	internal async Task<IReadOnlyList<string>> ListPlayersAsync() =>
		[.. (await _connection.ListServicesAsync()).Where(name => name.StartsWith(NamePrefix, StringComparison.Ordinal))];

	internal Task<Dictionary<string, object?>> GetAllAsync(string busName, string @interface)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(busName, ObjectPath, PropertiesInterface, "GetAll", "s", MessageFlags.None);
		writer.WriteString(@interface);
		return _connection.CallMethodAsync(
			writer.CreateMessage(),
			static (message, _) => ToDictionary(message.GetBodyReader().ReadDictionaryOfStringToVariantValue()),
			null);
	}

	internal Task<uint> GetProcessIdAsync(string busName)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetConnectionUnixProcessID", "s", MessageFlags.None);
		writer.WriteString(busName);
		return _connection.CallMethodAsync(writer.CreateMessage(), static (message, _) => message.GetBodyReader().ReadUInt32(), null);
	}

	internal Task CallAsync(string busName, string member)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(busName, ObjectPath, PlayerInterface, member, null, MessageFlags.None);
		return _connection.CallMethodAsync(writer.CreateMessage());
	}

	internal Task SetPositionAsync(string busName, string trackId, long micros)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(busName, ObjectPath, PlayerInterface, "SetPosition", "ox", MessageFlags.None);
		writer.WriteObjectPath(trackId);
		writer.WriteInt64(micros);
		return _connection.CallMethodAsync(writer.CreateMessage());
	}

	internal Task SeekByAsync(string busName, long offsetMicros)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(busName, ObjectPath, PlayerInterface, "Seek", "x", MessageFlags.None);
		writer.WriteInt64(offsetMicros);
		return _connection.CallMethodAsync(writer.CreateMessage());
	}

	internal Task SetAsync(string busName, string property, bool value) => SetAsync(busName, property, VariantValue.Bool(value));

	internal Task SetAsync(string busName, string property, string value) => SetAsync(busName, property, VariantValue.String(value));

	internal Task SetAsync(string busName, string property, double value) => SetAsync(busName, property, VariantValue.Double(value));

	public void Dispose() => _connection.Dispose();

	private Task SetAsync(string busName, string property, VariantValue value)
	{
		using var writer = _connection.GetMessageWriter();
		writer.WriteMethodCallHeader(busName, ObjectPath, PropertiesInterface, "Set", "ssv", MessageFlags.None);
		writer.WriteString(PlayerInterface);
		writer.WriteString(property);
		writer.WriteVariant(value);
		return _connection.CallMethodAsync(writer.CreateMessage());
	}

	private static Dictionary<string, object?> ToDictionary(Dictionary<string, VariantValue> values)
	{
		var result = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
		foreach (var (key, value) in values)
		{
			result[key] = ToObject(value);
		}

		return result;
	}

	private static object? ToObject(VariantValue value) => value.Type switch
	{
		VariantValueType.Variant => ToObject(value.GetVariantValue()),
		VariantValueType.String => value.GetString(),
		VariantValueType.ObjectPath => value.GetObjectPathAsString(),
		VariantValueType.Signature => null,
		VariantValueType.Bool => value.GetBool(),
		VariantValueType.Byte => (long)value.GetByte(),
		VariantValueType.Int16 => (long)value.GetInt16(),
		VariantValueType.UInt16 => (long)value.GetUInt16(),
		VariantValueType.Int32 => (long)value.GetInt32(),
		VariantValueType.UInt32 => (long)value.GetUInt32(),
		VariantValueType.Int64 => value.GetInt64(),
		VariantValueType.UInt64 => (long)Math.Min(value.GetUInt64(), long.MaxValue),
		VariantValueType.Double => value.GetDouble(),
		VariantValueType.Array => Enumerable.Range(0, value.Count).Select(index => ToObject(value.GetItem(index))).ToArray(),
		VariantValueType.Dictionary => ToDictionary(value),
		_ => null,
	};

	private static Dictionary<string, object?> ToDictionary(VariantValue dictionary)
	{
		var result = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
		for (var index = 0; index < dictionary.Count; index++)
		{
			var entry = dictionary.GetDictionaryEntry(index);
			if (ToObject(entry.Key) is string key)
			{
				result[key] = ToObject(entry.Value);
			}
		}

		return result;
	}
}
