// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Id3.Id3v2;

/// <summary>
/// Represents an ID3v2 tag containing frames with metadata.
/// </summary>
public sealed class Id3v2Tag : Tag
{
	const int FrameHeaderSize = 10;
	const int DefaultPaddingSize = 1024;

	readonly List<TextFrame> _frames = new (16);
	readonly List<PictureFrame> _pictures = new (2);

	/// <summary>
	/// Gets the ID3v2 version (2, 3, or 4).
	/// </summary>
	public int Version { get; }

	/// <summary>
	/// Gets the list of text frames in this tag.
	/// </summary>
	public IReadOnlyList<TextFrame> Frames => _frames;

	/// <summary>
	/// Gets the list of picture frames (album art) in this tag.
	/// </summary>
	public IReadOnlyList<PictureFrame> Pictures => _pictures;

	/// <summary>
	/// Gets a value indicating whether this tag contains any pictures.
	/// </summary>
	public bool HasPictures => _pictures.Count > 0;

	/// <summary>
	/// Gets or sets the front cover art.
	/// </summary>
	/// <remarks>
	/// Getting returns the first <see cref="PictureType.FrontCover"/> picture, or null if none exists.
	/// Setting replaces any existing front cover with the new picture.
	/// Set to null to remove all front cover pictures.
	/// </remarks>
	public PictureFrame? CoverArt {
		get => GetPicture (Core.PictureType.FrontCover);
		set => SetPicture (Core.PictureType.FrontCover, value);
	}

	/// <inheritdoc/>
	public override string? Title {
		get => GetTextFrame ("TIT2");
		set => SetTextFrame ("TIT2", value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetTextFrame ("TPE1");
		set => SetTextFrame ("TPE1", value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => GetTextFrame ("TALB");
		set => SetTextFrame ("TALB", value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => Version == 4 ? GetTextFrame ("TDRC") : GetTextFrame ("TYER");
		set {
			if (Version == 4)
				SetTextFrame ("TDRC", value);
			else
				SetTextFrame ("TYER", value);
		}
	}

	/// <inheritdoc/>
	public override string? Comment {
		// TODO: Implement COMM frame support
		get => null;
		set { }
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetTextFrame ("TCON");
		set => SetTextFrame ("TCON", value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get {
			var trackStr = GetTextFrame ("TRCK");
			if (string.IsNullOrEmpty (trackStr))
				return null;

			// Handle "5/12" format
#if NETSTANDARD2_0
			var slashIndex = trackStr!.IndexOf ('/');
#else
			var slashIndex = trackStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0)
				trackStr = trackStr.Substring (0, slashIndex);

			return uint.TryParse (trackStr, out var track) ? track : null;
		}
		set => SetTextFrame ("TRCK", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetTextFrame ("TPE2");
		set => SetTextFrame ("TPE2", value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get {
			var discStr = GetTextFrame ("TPOS");
			if (string.IsNullOrEmpty (discStr))
				return null;

			// Handle "2/3" format
#if NETSTANDARD2_0
			var slashIndex = discStr!.IndexOf ('/');
#else
			var slashIndex = discStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0)
				discStr = discStr.Substring (0, slashIndex);

			return uint.TryParse (discStr, out var disc) ? disc : null;
		}
		set => SetTextFrame ("TPOS", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Id3v2Tag"/> class.
	/// </summary>
	/// <param name="version">The ID3v2 version to use.</param>
	public Id3v2Tag (Id3v2Version version = Id3v2Version.V24)
	{
		Version = (int)version;
	}

	Id3v2Tag (int version)
	{
		Version = version;
	}

	/// <summary>
	/// Attempts to read an ID3v2 tag from the provided data.
	/// </summary>
	/// <param name="data">The data to parse.</param>
	/// <returns>A result indicating success, failure, or not found.</returns>
	public static TagReadResult<Id3v2Tag> Read (ReadOnlySpan<byte> data)
	{
		var headerResult = Id3v2Header.Read (data);

		if (headerResult.IsNotFound)
			return TagReadResult<Id3v2Tag>.NotFound ();

		if (!headerResult.IsSuccess)
			return TagReadResult<Id3v2Tag>.Failure (headerResult.Error!);

		var header = headerResult.Header;
		var tag = new Id3v2Tag (header.MajorVersion);

		// Skip header and parse frames
		var frameData = data.Slice (Id3v2Header.HeaderSize);
		var remaining = (int)header.TagSize;

		// Skip extended header if present
		if (header.HasExtendedHeader && remaining >= 4) {
			var extHeaderSize = GetExtendedHeaderSize (frameData, header.MajorVersion);
			if (extHeaderSize > 0 && extHeaderSize <= remaining) {
				frameData = frameData.Slice (extHeaderSize);
				remaining -= extHeaderSize;
			}
		}

		while (remaining >= FrameHeaderSize) {
			// Check for padding (zeros)
			if (frameData[0] == 0)
				break;

			// Read frame header
			var frameId = GetFrameId (frameData);
			if (string.IsNullOrEmpty (frameId))
				break;

			var frameSize = GetFrameSize (frameData.Slice (4, 4), header.MajorVersion);
			if (frameSize <= 0 || frameSize > remaining - FrameHeaderSize)
				break;

			// Parse frame content
			var frameContent = frameData.Slice (FrameHeaderSize, frameSize);

			// Handle text frames (T***)
			if (frameId[0] == 'T' && frameId != "TXXX") {
				var frameResult = TextFrame.Read (frameId, frameContent, (Id3v2Version)header.MajorVersion);
				if (frameResult.IsSuccess)
					tag._frames.Add (frameResult.Frame!);
			}
			// Handle picture frames (APIC)
			else if (frameId == "APIC") {
				var pictureResult = PictureFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (pictureResult.IsSuccess)
					tag._pictures.Add (pictureResult.Frame!);
			}

			// Move to next frame
			var totalFrameSize = FrameHeaderSize + frameSize;
			frameData = frameData.Slice (totalFrameSize);
			remaining -= totalFrameSize;
		}

		return TagReadResult<Id3v2Tag>.Success (tag, (int)header.TotalSize);
	}

	/// <summary>
	/// Gets the value of a text frame by ID.
	/// </summary>
	/// <param name="frameId">The frame ID (e.g., TIT2, TPE1).</param>
	/// <returns>The text value, or null if not found.</returns>
	public string? GetTextFrame (string frameId)
	{
		// Use for loop instead of LINQ for better performance
		for (var i = 0; i < _frames.Count; i++) {
			if (_frames[i].Id == frameId)
				return _frames[i].Text;
		}
		return null;
	}

	/// <summary>
	/// Sets or creates a text frame.
	/// </summary>
	/// <param name="frameId">The frame ID.</param>
	/// <param name="value">The text value, or null to remove.</param>
	void SetTextFrame (string frameId, string? value)
	{
		// Remove existing frame
		_frames.RemoveAll (f => f.Id == frameId);

		// Add new frame if value is not empty
		if (!string.IsNullOrEmpty (value))
			_frames.Add (new TextFrame (frameId, value!, TextEncodingType.Utf8));
	}

	/// <summary>
	/// Adds a picture frame to the tag.
	/// </summary>
	/// <param name="picture">The picture frame to add.</param>
	public void AddPicture (PictureFrame picture)
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
	public void RemovePictures (Core.PictureType pictureType)
	{
		_pictures.RemoveAll (p => p.PictureType == pictureType);
	}

	/// <summary>
	/// Gets the first picture of a specific type.
	/// </summary>
	/// <param name="pictureType">The picture type to find.</param>
	/// <returns>The picture frame, or null if not found.</returns>
	public PictureFrame? GetPicture (Core.PictureType pictureType)
	{
		// Use for loop instead of LINQ for better performance
		for (var i = 0; i < _pictures.Count; i++) {
			if (_pictures[i].PictureType == pictureType)
				return _pictures[i];
		}
		return null;
	}

	/// <summary>
	/// Sets or removes a picture of a specific type.
	/// </summary>
	/// <param name="pictureType">The picture type to set.</param>
	/// <param name="picture">The picture to set, or null to remove all pictures of this type.</param>
	/// <remarks>
	/// This method performs an atomic replace: it removes all existing pictures of the
	/// specified type and adds the new picture (if not null) in a single operation.
	/// </remarks>
	public void SetPicture (Core.PictureType pictureType, PictureFrame? picture)
	{
		RemovePictures (pictureType);
		if (picture is not null)
			_pictures.Add (picture);
	}

	/// <summary>
	/// Removes all pictures from this tag.
	/// </summary>
	public void RemoveAllPictures ()
	{
		_pictures.Clear ();
	}

	/// <inheritdoc/>
	public override BinaryData Render () => Render (DefaultPaddingSize);

	/// <summary>
	/// Renders the tag to binary data with specified padding.
	/// </summary>
	/// <param name="paddingSize">The amount of padding to include.</param>
	/// <returns>The rendered tag data.</returns>
	public BinaryData Render (int paddingSize)
	{
		// Render all frames and calculate size in single pass
		var frameDataList = new List<BinaryData> ((_frames.Count + _pictures.Count) * 2);
		var framesSize = 0;

		for (var i = 0; i < _frames.Count; i++) {
			var content = _frames[i].RenderContent ();
			var frameHeader = RenderFrameHeader (_frames[i].Id, content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render picture frames
		for (var i = 0; i < _pictures.Count; i++) {
			var content = _pictures[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("APIC", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		var totalSize = framesSize + paddingSize;

		// Render header
		var header = new Id3v2Header (
			(byte)Version,
			0,
			Id3v2HeaderFlags.None,
			(uint)totalSize);

		using var builder = new BinaryDataBuilder (Id3v2Header.HeaderSize + totalSize);
		builder.Add (header.Render ());

		foreach (var frameData in frameDataList)
			builder.Add (frameData);

		builder.AddZeros (paddingSize);

		return builder.ToBinaryData ();
	}

	BinaryData RenderFrameHeader (string frameId, int contentSize)
	{
		using var builder = new BinaryDataBuilder (FrameHeaderSize);

		// Frame ID (4 bytes)
		var idBytes = System.Text.Encoding.ASCII.GetBytes (frameId);
		builder.Add (idBytes.AsSpan ().Slice (0, 4));

		// Size (syncsafe for v2.4, big-endian for v2.3)
		if (Version == 4)
			builder.AddSyncSafeUInt32 ((uint)contentSize);
		else
			builder.AddUInt32BE ((uint)contentSize);

		// Flags (2 bytes, zeros)
		builder.AddUInt16BE (0);

		return builder.ToBinaryData ();
	}

	/// <inheritdoc/>
	public override void Clear ()
	{
		_frames.Clear ();
		_pictures.Clear ();
	}

	static string GetFrameId (ReadOnlySpan<byte> data)
	{
		if (data.Length < 4)
			return string.Empty;

		// Frame ID must be uppercase A-Z or 0-9
		for (var i = 0; i < 4; i++) {
			var c = data[i];
			if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
				return string.Empty;
		}

		return System.Text.Encoding.ASCII.GetString (data.Slice (0, 4));
	}

	static int GetFrameSize (ReadOnlySpan<byte> data, byte version)
	{
		if (version == 4) {
			// Syncsafe integer (7 bits per byte, max 28 bits = 268MB)
			return ((data[0] & 0x7F) << 21) |
				   ((data[1] & 0x7F) << 14) |
				   ((data[2] & 0x7F) << 7) |
				   (data[3] & 0x7F);
		} else {
			// Big-endian 32-bit unsigned, cast to uint to avoid overflow when MSB >= 0x80.
			// Values > int.MaxValue are unrealistic for frame sizes and will fail
			// bounds checking in the caller, so we safely cast back to int.
			return (int)(((uint)data[0] << 24) |
						 ((uint)data[1] << 16) |
						 ((uint)data[2] << 8) |
						 (uint)data[3]);
		}
	}

	static int GetExtendedHeaderSize (ReadOnlySpan<byte> data, byte version)
	{
		if (data.Length < 4)
			return 0;

		if (version == 4) {
			// v2.4: Size is syncsafe and INCLUDES the size field itself
			return ((data[0] & 0x7F) << 21) |
				   ((data[1] & 0x7F) << 14) |
				   ((data[2] & 0x7F) << 7) |
				   (data[3] & 0x7F);
		} else {
			// v2.3: Size is big-endian and EXCLUDES the size field
			// Total size = 4 (size field) + extended header size
			var extSize = (int)(((uint)data[0] << 24) |
							    ((uint)data[1] << 16) |
							    ((uint)data[2] << 8) |
							    (uint)data[3]);
			return extSize + 4;
		}
	}
}
