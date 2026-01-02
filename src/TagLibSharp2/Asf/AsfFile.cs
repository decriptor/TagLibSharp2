// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;

using TagLibSharp2.Core;

namespace TagLibSharp2.Asf;

/// <summary>
/// Represents an ASF/WMA audio file.
/// </summary>
public sealed class AsfFile
{
	/// <summary>
	/// Gets the ASF tag.
	/// </summary>
	public AsfTag Tag { get; }

	/// <summary>
	/// Gets the audio properties.
	/// </summary>
	public AudioProperties AudioProperties { get; }

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	AsfFile (AsfTag tag, AudioProperties audioProperties)
	{
		Tag = tag;
		AudioProperties = audioProperties;
	}

	// ═══════════════════════════════════════════════════════════════
	// Convenience Properties (delegating to Tag)
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Gets or sets the title (delegates to Tag.Title).
	/// </summary>
	public string? Title {
		get => Tag.Title;
		set => Tag.Title = value;
	}

	/// <summary>
	/// Gets or sets the artist (delegates to Tag.Artist).
	/// </summary>
	public string? Artist {
		get => Tag.Artist;
		set => Tag.Artist = value;
	}

	/// <summary>
	/// Gets or sets the album (delegates to Tag.Album).
	/// </summary>
	public string? Album {
		get => Tag.Album;
		set => Tag.Album = value;
	}

	// ═══════════════════════════════════════════════════════════════
	// Parsing
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Reads an ASF file from binary data.
	/// </summary>
	public static AsfFileReadResult Read (ReadOnlySpan<byte> data)
	{
		// Minimum size: Header GUID (16) + Size (8) + Count (4) + Reserved (2) = 30
		if (data.Length < 30)
			return AsfFileReadResult.Failure ("Data too short for ASF header");

		var offset = 0;

		// Verify header GUID
		var headerGuidResult = AsfGuid.Parse (data[offset..]);
		if (!headerGuidResult.IsSuccess)
			return AsfFileReadResult.Failure ($"Failed to parse header GUID: {headerGuidResult.Error}");

		if (headerGuidResult.Value != AsfGuids.HeaderObject)
			return AsfFileReadResult.Failure ("Invalid ASF header: not an ASF file");

		offset += 16;

		// Read header size
		var headerSize = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
		offset += 8;

		if (headerSize > (ulong)data.Length)
			return AsfFileReadResult.Failure ("Header size exceeds available data");

		// Read child object count
		var childCount = BinaryPrimitives.ReadUInt32LittleEndian (data[offset..]);
		offset += 4;

		// Skip reserved bytes (2)
		offset += 2;

		// Parse child objects
		AsfContentDescription? contentDesc = null;
		AsfExtendedContentDescription? extendedDesc = null;
		AsfFileProperties? fileProps = null;
		AsfStreamProperties? streamProps = null;

		for (uint i = 0; i < childCount && offset < data.Length - 24; i++) {
			// Read object GUID
			var guidResult = AsfGuid.Parse (data[offset..]);
			if (!guidResult.IsSuccess)
				break; // Stop parsing but don't fail completely

			var objectGuid = guidResult.Value;
			offset += 16;

			// Read object size
			if (offset + 8 > data.Length)
				break;

			var objectSize = BinaryPrimitives.ReadUInt64LittleEndian (data[offset..]);
			offset += 8;

			// Content size is object size minus GUID (16) and size field (8)
			// Guard against overflow: objectSize could be huge
			if (objectSize < 24 || objectSize > int.MaxValue)
				break;
			var contentSize = (int)(objectSize - 24);
			// Use subtraction to avoid integer overflow in comparison
			if (contentSize > data.Length - offset)
				break;

			var objectContent = data.Slice (offset, contentSize);

			// Parse known objects
			if (objectGuid == AsfGuids.ContentDescriptionObject) {
				var result = AsfContentDescription.Parse (objectContent);
				if (result.IsSuccess)
					contentDesc = result.Value;
			} else if (objectGuid == AsfGuids.ExtendedContentDescriptionObject) {
				var result = AsfExtendedContentDescription.Parse (objectContent);
				if (result.IsSuccess)
					extendedDesc = result.Value;
			} else if (objectGuid == AsfGuids.FilePropertiesObject) {
				var result = AsfFileProperties.Parse (objectContent);
				if (result.IsSuccess)
					fileProps = result.Value;
			} else if (objectGuid == AsfGuids.StreamPropertiesObject) {
				var result = AsfStreamProperties.Parse (objectContent);
				if (result.IsSuccess && result.Value.IsAudio)
					streamProps = result.Value;
			}

			offset += contentSize;
		}

		// Create tag
		var tag = new AsfTag (contentDesc, extendedDesc);

		// Create audio properties
		var audioProps = CreateAudioProperties (fileProps, streamProps);

		var file = new AsfFile (tag, audioProps);
		return AsfFileReadResult.Success (file);
	}

	/// <summary>
	/// Reads an ASF file from a file path.
	/// </summary>
	public static AsfFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return AsfFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!);
		if (result.IsSuccess) {
			result.Value.SourcePath = path;
			result.Value._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Reads an ASF file from a file path asynchronously.
	/// </summary>
	public static async Task<AsfFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken).ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return AsfFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!);
		if (result.IsSuccess) {
			result.Value.SourcePath = path;
			result.Value._sourceFileSystem = fs;
		}
		return result;
	}

	static AudioProperties CreateAudioProperties (AsfFileProperties? fileProps, AsfStreamProperties? streamProps)
	{
		var duration = fileProps?.Duration ?? TimeSpan.Zero;
		var bitrate = (int)((fileProps?.MaxBitrate ?? 0) / 1000);
		var sampleRate = (int)(streamProps?.SampleRate ?? 0);
		var channels = streamProps?.Channels ?? 0;
		var bitsPerSample = streamProps?.BitsPerSample ?? 0;
		var codec = streamProps?.CodecName;

		return new AudioProperties (duration, bitrate, sampleRate, bitsPerSample, channels, codec);
	}
}

/// <summary>
/// Result of reading an ASF file.
/// </summary>
public readonly struct AsfFileReadResult : IEquatable<AsfFileReadResult>
{
	/// <summary>
	/// Gets the parsed ASF file.
	/// </summary>
	public AsfFile Value { get; }

	/// <summary>
	/// Gets the error message if reading failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets whether reading was successful.
	/// </summary>
	public bool IsSuccess => Error is null;

	AsfFileReadResult (AsfFile value, string? error)
	{
		Value = value;
		Error = error;
	}

	/// <summary>
	/// Creates a successful read result.
	/// </summary>
	public static AsfFileReadResult Success (AsfFile value) => new (value, null);

	/// <summary>
	/// Creates a failed read result.
	/// </summary>
	public static AsfFileReadResult Failure (string error) => new (null!, error);

	/// <inheritdoc/>
	public bool Equals (AsfFileReadResult other) => Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj)
		=> obj is AsfFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Error);

	/// <summary>
	/// Equality operator.
	/// </summary>
	public static bool operator == (AsfFileReadResult left, AsfFileReadResult right)
		=> left.Equals (right);

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator != (AsfFileReadResult left, AsfFileReadResult right)
		=> !left.Equals (right);
}
