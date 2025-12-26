// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents a FLAC audio file with its metadata blocks.
/// </summary>
/// <remarks>
/// <para>
/// FLAC files contain one or more metadata blocks before the audio frames.
/// The STREAMINFO block is always first and is the only required block.
/// This class provides access to VORBIS_COMMENT (tags) and PICTURE (album art) blocks.
/// </para>
/// <para>
/// File structure:
/// </para>
/// <code>
/// [magic:4 "fLaC"]
/// [STREAMINFO block] (required, always first)
/// [other metadata blocks...]
/// [audio frames...]
/// </code>
/// <para>
/// Reference: https://xiph.org/flac/format.html
/// </para>
/// </remarks>
public sealed class FlacFile
{
	const int MagicSize = 4;
	static readonly byte[] FlacMagic = { 0x66, 0x4C, 0x61, 0x43 }; // "fLaC"

	readonly List<FlacPicture> _pictures = new (2);

	/// <summary>
	/// Gets the size of the metadata section in bytes (magic + all metadata blocks).
	/// </summary>
	public int MetadataSize { get; private set; }

	/// <summary>
	/// Gets the raw STREAMINFO block data.
	/// </summary>
	/// <remarks>
	/// STREAMINFO is preserved as raw bytes since it's not typically modified.
	/// </remarks>
	public BinaryData StreamInfoData { get; }

	/// <summary>
	/// Gets or sets the Vorbis Comment block containing metadata tags.
	/// </summary>
	/// <remarks>
	/// May be null if the file has no VORBIS_COMMENT block.
	/// Will be automatically created when setting any tag property.
	/// </remarks>
	public VorbisComment? VorbisComment { get; set; }

	/// <summary>
	/// Gets the list of PICTURE blocks.
	/// </summary>
	public IReadOnlyList<FlacPicture> Pictures => _pictures;

	/// <summary>
	/// Gets or sets the title tag.
	/// </summary>
	/// <remarks>
	/// Delegates to VorbisComment. Setting creates VorbisComment if null.
	/// </remarks>
	public string? Title {
		get => VorbisComment?.Title;
		set => EnsureVorbisComment ().Title = value;
	}

	/// <summary>
	/// Gets or sets the artist tag.
	/// </summary>
	public string? Artist {
		get => VorbisComment?.Artist;
		set => EnsureVorbisComment ().Artist = value;
	}

	/// <summary>
	/// Gets or sets the album tag.
	/// </summary>
	public string? Album {
		get => VorbisComment?.Album;
		set => EnsureVorbisComment ().Album = value;
	}

	/// <summary>
	/// Gets or sets the year tag.
	/// </summary>
	public string? Year {
		get => VorbisComment?.Year;
		set => EnsureVorbisComment ().Year = value;
	}

	/// <summary>
	/// Gets or sets the genre tag.
	/// </summary>
	public string? Genre {
		get => VorbisComment?.Genre;
		set => EnsureVorbisComment ().Genre = value;
	}

	/// <summary>
	/// Gets or sets the track number.
	/// </summary>
	public uint? Track {
		get => VorbisComment?.Track;
		set => EnsureVorbisComment ().Track = value;
	}

	/// <summary>
	/// Gets or sets the comment tag.
	/// </summary>
	public string? Comment {
		get => VorbisComment?.Comment;
		set => EnsureVorbisComment ().Comment = value;
	}

	/// <summary>
	/// Gets the audio properties (duration, bitrate, sample rate, etc.).
	/// </summary>
	public AudioProperties Properties { get; }

	FlacFile (BinaryData streamInfoData, AudioProperties properties)
	{
		StreamInfoData = streamInfoData;
		Properties = properties;
	}

	/// <summary>
	/// Adds a picture to this file.
	/// </summary>
	/// <param name="picture">The picture to add.</param>
	public void AddPicture (FlacPicture picture)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (picture is null)
			throw new ArgumentNullException (nameof (picture));
#else
		ArgumentNullException.ThrowIfNull (picture);
#endif
		_pictures.Add (picture);
	}

	/// <summary>
	/// Removes all pictures of a specific type.
	/// </summary>
	/// <param name="pictureType">The picture type to remove.</param>
	public void RemovePictures (PictureType pictureType)
	{
		_pictures.RemoveAll (p => p.PictureType == pictureType);
	}

	/// <summary>
	/// Removes all pictures from this file.
	/// </summary>
	public void RemoveAllPictures ()
	{
		_pictures.Clear ();
	}

	/// <summary>
	/// Attempts to read a FLAC file from a file path.
	/// </summary>
	/// <param name="path">The path to the FLAC file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static FlacFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return FlacFileReadResult.Failure (readResult.Error!);

		return Read (readResult.Data!);
	}

	/// <summary>
	/// Asynchronously attempts to read a FLAC file from a file path.
	/// </summary>
	/// <param name="path">The path to the FLAC file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
	public static async Task<FlacFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return FlacFileReadResult.Failure (readResult.Error!);

		return Read (readResult.Data!);
	}

	/// <summary>
	/// Attempts to read a FLAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static FlacFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MagicSize)
			return FlacFileReadResult.Failure ("Data too short for FLAC header");

		// Verify magic
		if (data[0] != FlacMagic[0] || data[1] != FlacMagic[1] ||
			data[2] != FlacMagic[2] || data[3] != FlacMagic[3])
			return FlacFileReadResult.Failure ("Invalid FLAC magic (expected 'fLaC')");

		var offset = MagicSize;

		// Read STREAMINFO (must be first)
		if (offset + FlacMetadataBlockHeader.HeaderSize > data.Length)
			return FlacFileReadResult.Failure ("Data too short for STREAMINFO header");

		var headerResult = FlacMetadataBlockHeader.Read (data.Slice (offset, FlacMetadataBlockHeader.HeaderSize));
		if (!headerResult.IsSuccess)
			return FlacFileReadResult.Failure (headerResult.Error!);

		if (headerResult.Header.BlockType != FlacBlockType.StreamInfo)
			return FlacFileReadResult.Failure ("First block must be STREAMINFO");

		// Per RFC 9639, STREAMINFO is always exactly 34 bytes
		const int StreamInfoSize = 34;
		if (headerResult.Header.DataLength != StreamInfoSize)
			return FlacFileReadResult.Failure ($"Invalid STREAMINFO size (expected {StreamInfoSize}, got {headerResult.Header.DataLength})");

		offset += FlacMetadataBlockHeader.HeaderSize;

		if (offset + headerResult.Header.DataLength > data.Length)
			return FlacFileReadResult.Failure ("STREAMINFO data extends beyond file");

		var streamInfoSlice = data.Slice (offset, headerResult.Header.DataLength);
		var streamInfoData = new BinaryData (streamInfoSlice);
		var properties = ParseStreamInfo (streamInfoSlice);
		offset += headerResult.Header.DataLength;

		var file = new FlacFile (streamInfoData, properties);
		var lastBlock = headerResult.Header.IsLast;

		// Read remaining metadata blocks
		while (!lastBlock && offset + FlacMetadataBlockHeader.HeaderSize <= data.Length) {
			headerResult = FlacMetadataBlockHeader.Read (data.Slice (offset, FlacMetadataBlockHeader.HeaderSize));
			if (!headerResult.IsSuccess)
				break;

			offset += FlacMetadataBlockHeader.HeaderSize;

			if (offset + headerResult.Header.DataLength > data.Length)
				break;

			var blockData = data.Slice (offset, headerResult.Header.DataLength);
			offset += headerResult.Header.DataLength;
			lastBlock = headerResult.Header.IsLast;

			switch (headerResult.Header.BlockType) {
				case FlacBlockType.VorbisComment:
					var commentResult = VorbisComment.Read (blockData);
					if (commentResult.IsSuccess)
						file.VorbisComment = commentResult.Tag;
					break;

				case FlacBlockType.Picture:
					var pictureResult = FlacPicture.Read (blockData);
					if (pictureResult.IsSuccess)
						file._pictures.Add (pictureResult.Picture!);
					break;

					// Other block types (PADDING, APPLICATION, SEEKTABLE, CUESHEET) are skipped
			}
		}

		file.MetadataSize = offset;
		return FlacFileReadResult.Success (file, offset);
	}

	VorbisComment EnsureVorbisComment ()
	{
		VorbisComment ??= new VorbisComment ("TagLibSharp2");
		return VorbisComment;
	}

	/// <summary>
	/// Parses the STREAMINFO block to extract audio properties.
	/// </summary>
	/// <remarks>
	/// STREAMINFO layout (34 bytes total):
	/// <code>
	/// Bytes 0-1:   Minimum block size (16 bits)
	/// Bytes 2-3:   Maximum block size (16 bits)
	/// Bytes 4-6:   Minimum frame size (24 bits)
	/// Bytes 7-9:   Maximum frame size (24 bits)
	/// Bytes 10-13: Sample rate (20 bits) | channels-1 (3 bits) | bits per sample-1 (5 bits) | total samples upper 4 bits
	/// Bytes 14-17: Total samples lower 32 bits
	/// Bytes 18-33: MD5 signature (128 bits)
	/// </code>
	/// </remarks>
	static AudioProperties ParseStreamInfo (ReadOnlySpan<byte> data)
	{
		if (data.Length < 18) // Minimum needed for audio properties
			return AudioProperties.Empty;

		// Bytes 10-13 contain sample rate (20 bits), channels (3 bits), bps (5 bits), and upper 4 bits of total samples
		// Big-endian: bytes 10-12 for sample rate, byte 12 also has channels, byte 13 has bps and upper samples
		var sampleRate = ((data[10] << 12) | (data[11] << 4) | (data[12] >> 4)) & 0xFFFFF;

		var channels = ((data[12] >> 1) & 0x07) + 1; // 3 bits, stored as channels-1

		var bitsPerSample = (((data[12] & 0x01) << 4) | (data[13] >> 4)) + 1; // 5 bits, stored as bps-1

		// Total samples: upper 4 bits from byte 13, lower 32 bits from bytes 14-17
		var totalSamplesUpper = (ulong)(data[13] & 0x0F);
		var totalSamplesLower = (ulong)((data[14] << 24) | (data[15] << 16) | (data[16] << 8) | data[17]);
		var totalSamples = (totalSamplesUpper << 32) | totalSamplesLower;

		return AudioProperties.FromFlac (totalSamples, sampleRate, bitsPerSample, channels);
	}
}

/// <summary>
/// Represents the result of reading a <see cref="FlacFile"/> from binary data.
/// </summary>
public readonly struct FlacFileReadResult : IEquatable<FlacFileReadResult>
{
	/// <summary>
	/// Gets the parsed file, or null if parsing failed.
	/// </summary>
	public FlacFile? File { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data (metadata section size).
	/// </summary>
	public int BytesConsumed { get; }

	FlacFileReadResult (FlacFile? file, string? error, int bytesConsumed)
	{
		File = file;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="file">The parsed file.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static FlacFileReadResult Success (FlacFile file, int bytesConsumed) =>
		new (file, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static FlacFileReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (FlacFileReadResult other) =>
		ReferenceEquals (File, other.File) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FlacFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (FlacFileReadResult left, FlacFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (FlacFileReadResult left, FlacFileReadResult right) =>
		!left.Equals (right);
}
