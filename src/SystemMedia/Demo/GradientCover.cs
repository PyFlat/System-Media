using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SystemMedia.Demo;

// A diagonal two-tone gradient, so a demo track without a cover file still has one.
internal static class GradientCover
{
	private const int Size = 512;

	internal static byte[] Png(int hue)
	{
		var (r1, g1, b1) = FromHue(hue, lightness: 0.62);
		var (r2, g2, b2) = FromHue(hue + 50, lightness: 0.28);

		var raw = new byte[Size * (1 + Size * 3)];
		var offset = 0;
		for (var y = 0; y < Size; y++)
		{
			raw[offset++] = 0;
			for (var x = 0; x < Size; x++)
			{
				var t = (x + y) / (2.0 * (Size - 1));
				raw[offset++] = Mix(r1, r2, t);
				raw[offset++] = Mix(g1, g2, t);
				raw[offset++] = Mix(b1, b2, t);
			}
		}

		using var compressed = new MemoryStream();
		using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			zlib.Write(raw);
		}

		var header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, Size);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Size);
		header[8] = 8;
		header[9] = 2;

		using var png = new MemoryStream();
		png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		WriteChunk(png, "IHDR", header);
		WriteChunk(png, "IDAT", compressed.ToArray());
		WriteChunk(png, "IEND", []);
		return png.ToArray();
	}

	private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));

	private static (byte R, byte G, byte B) FromHue(int hue, double lightness)
	{
		const double Saturation = 0.55;
		var h = ((hue % 360) + 360) % 360 / 60.0;
		var chroma = (1 - Math.Abs((2 * lightness) - 1)) * Saturation;
		var x = chroma * (1 - Math.Abs((h % 2) - 1));
		var (r, g, b) = (int)h switch
		{
			0 => (chroma, x, 0.0),
			1 => (x, chroma, 0.0),
			2 => (0.0, chroma, x),
			3 => (0.0, x, chroma),
			4 => (x, 0.0, chroma),
			_ => (chroma, 0.0, x),
		};
		var m = lightness - (chroma / 2);
		return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
	}

	private static void WriteChunk(Stream png, string type, byte[] data)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
		png.Write(length);

		var typeAndData = new byte[4 + data.Length];
		Encoding.ASCII.GetBytes(type, typeAndData);
		data.CopyTo(typeAndData, 4);
		png.Write(typeAndData);

		Span<byte> crc = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
		png.Write(crc);
	}

	private static uint Crc32(byte[] bytes)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var value in bytes)
		{
			crc ^= value;
			for (var bit = 0; bit < 8; bit++)
			{
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
			}
		}

		return ~crc;
	}
}
