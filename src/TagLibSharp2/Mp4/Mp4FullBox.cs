// Copyright (c) 2025 Stephen Shaw and contributors

using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// Represents an ISO 14496-12 FullBox, which extends Box with version and flags.
/// Many MP4 boxes (mvhd, mdhd, tkhd, etc.) are FullBoxes.
/// </summary>
public class Mp4FullBox : Mp4Box
{
	/// <summary>
	/// Gets the box version (typically 0 or 1).
	/// Version affects the interpretation of the box data.
	/// </summary>
	public byte Version { get; }

	/// <summary>
	/// Gets the 24-bit flags field.
	/// Interpretation is box-specific.
	/// </summary>
	public uint Flags { get; }

	/// <summary>
	/// Gets the box data excluding the version and flags (4 bytes).
	/// </summary>
	public BinaryData ContentData { get; }

	/// <summary>
	/// Creates a new MP4 FullBox.
	/// </summary>
	/// <param name="type">The 4-character box type code.</param>
	/// <param name="version">The box version.</param>
	/// <param name="flags">The 24-bit flags (max 0xFFFFFF).</param>
	/// <param name="contentData">The box data excluding version/flags header.</param>
	/// <param name="children">Child boxes for container boxes.</param>
	/// <param name="usesExtendedSize">Whether this box uses 64-bit size.</param>
	public Mp4FullBox(
		string type,
		byte version,
		uint flags,
		BinaryData contentData,
		IReadOnlyList<Mp4Box>? children = null,
		bool usesExtendedSize = false)
		: base(type, BuildFullBoxData(version, flags, contentData), children, usesExtendedSize)
	{
		if (flags > 0xFFFFFF)
			throw new ArgumentOutOfRangeException(nameof(flags), "Flags must fit in 24 bits (max 0xFFFFFF)");

		Version = version;
		Flags = flags;
		ContentData = contentData;
	}

	/// <summary>
	/// Parses a FullBox from raw box data.
	/// </summary>
	/// <param name="type">The box type.</param>
	/// <param name="data">The box data (must be at least 4 bytes for version+flags).</param>
	/// <param name="children">Child boxes if this is a container.</param>
	/// <param name="usesExtendedSize">Whether the box uses extended size.</param>
	/// <returns>A parsed FullBox instance.</returns>
	public static Mp4FullBox Parse(string type, BinaryData data, IReadOnlyList<Mp4Box>? children = null, bool usesExtendedSize = false)
	{
		if (data.Length < 4)
			throw new ArgumentException("FullBox data must be at least 4 bytes (version + flags)", nameof(data));

		var version = data[0];
		var flags = data.ToUInt24BE(1);
		var contentData = data.Slice(4);

		return new Mp4FullBox(type, version, flags, contentData, children, usesExtendedSize);
	}

	/// <summary>
	/// Builds the complete box data by prepending version and flags to content data.
	/// </summary>
	static BinaryData BuildFullBoxData(byte version, uint flags, BinaryData contentData)
	{
		using var builder = new BinaryDataBuilder(4 + contentData.Length);
		builder.Add(version);
		builder.AddUInt24BE(flags);
		builder.Add(contentData);
		return builder.ToBinaryData();
	}

	/// <inheritdoc />
	public override string ToString() => $"{Type} v{Version} flags=0x{Flags:X6} ({TotalSize} bytes{(IsContainer ? $", {Children.Count} children" : "")})";
}
