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
	readonly List<CommentFrame> _comments = new (2);
	readonly List<UserTextFrame> _userTextFrames = new (8);
	readonly List<LyricsFrame> _lyricsFrames = new (2);
	readonly List<UniqueFileIdFrame> _uniqueFileIdFrames = new (2);

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
	/// <remarks>
	/// In ID3v2.4, uses the TDOR (Original Release Time) frame.
	/// In ID3v2.3, uses the TORY (Original Release Year) frame.
	/// When reading, falls back to the other version's frame if the primary is not found.
	/// </remarks>
	public override string? OriginalReleaseDate {
		get {
			// Try version-appropriate frame first, then fallback
			if (Version == 4) {
				return GetTextFrame ("TDOR") ?? GetTextFrame ("TORY");
			}
			return GetTextFrame ("TORY") ?? GetTextFrame ("TDOR");
		}
		set {
			if (Version == 4)
				SetTextFrame ("TDOR", value);
			else
				SetTextFrame ("TORY", value);
		}
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetComment ();
		set => SetComment (value);
	}

	/// <summary>
	/// Gets the list of comment frames in this tag.
	/// </summary>
	public IReadOnlyList<CommentFrame> Comments => _comments;

	/// <summary>
	/// Gets the list of user-defined text frames (TXXX) in this tag.
	/// </summary>
	public IReadOnlyList<UserTextFrame> UserTextFrames => _userTextFrames;

	/// <summary>
	/// Gets the list of lyrics frames (USLT) in this tag.
	/// </summary>
	public IReadOnlyList<LyricsFrame> LyricsFrames => _lyricsFrames;

	/// <summary>
	/// Gets the list of unique file identifier frames (UFID) in this tag.
	/// </summary>
	public IReadOnlyList<UniqueFileIdFrame> UniqueFileIdFrames => _uniqueFileIdFrames;

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

	/// <inheritdoc/>
	public override string? Composer {
		get => GetTextFrame ("TCOM");
		set => SetTextFrame ("TCOM", value);
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var bpmStr = GetTextFrame ("TBPM");
			if (string.IsNullOrEmpty (bpmStr))
				return null;
			return uint.TryParse (bpmStr, out var bpm) ? bpm : null;
		}
		set => SetTextFrame ("TBPM", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetTextFrame ("TPE3");
		set => SetTextFrame ("TPE3", value);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => GetTextFrame ("TCOP");
		set => SetTextFrame ("TCOP", value);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => GetTextFrame ("TCMP") == "1";
		set {
			if (value)
				SetTextFrame ("TCMP", "1");
			else
				SetTextFrame ("TCMP", null);
		}
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetTextFrame ("TSRC");
		set => SetTextFrame ("TSRC", value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetTextFrame ("TPUB");
		set => SetTextFrame ("TPUB", value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetTextFrame ("TSOA");
		set => SetTextFrame ("TSOA", value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetTextFrame ("TSOP");
		set => SetTextFrame ("TSOP", value);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetTextFrame ("TSOT");
		set => SetTextFrame ("TSOT", value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetTextFrame ("TSO2");
		set => SetTextFrame ("TSO2", value);
	}

	/// <inheritdoc/>
	public override uint? TotalTracks {
		get {
			var trackStr = GetTextFrame ("TRCK");
			if (string.IsNullOrEmpty (trackStr))
				return null;

#if NETSTANDARD2_0
			var slashIndex = trackStr!.IndexOf ('/');
#else
			var slashIndex = trackStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0) {
				var totalPart = trackStr.Substring (slashIndex + 1);
				if (uint.TryParse (totalPart, out var total))
					return total;
			}
			return null;
		}
		set {
			var currentTrack = Track ?? 1;
			if (value.HasValue)
				SetTextFrame ("TRCK", $"{currentTrack}/{value.Value}");
			else
				SetTextFrame ("TRCK", currentTrack.ToString (System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override uint? TotalDiscs {
		get {
			var discStr = GetTextFrame ("TPOS");
			if (string.IsNullOrEmpty (discStr))
				return null;

#if NETSTANDARD2_0
			var slashIndex = discStr!.IndexOf ('/');
#else
			var slashIndex = discStr!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0) {
				var totalPart = discStr.Substring (slashIndex + 1);
				if (uint.TryParse (totalPart, out var total))
					return total;
			}
			return null;
		}
		set {
			var currentDisc = DiscNumber ?? 1;
			if (value.HasValue)
				SetTextFrame ("TPOS", $"{currentDisc}/{value.Value}");
			else
				SetTextFrame ("TPOS", currentDisc.ToString (System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetLyrics ();
		set => SetLyrics (value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetUserText ("REPLAYGAIN_TRACK_GAIN");
		set => SetUserText ("REPLAYGAIN_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetUserText ("REPLAYGAIN_TRACK_PEAK");
		set => SetUserText ("REPLAYGAIN_TRACK_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetUserText ("REPLAYGAIN_ALBUM_GAIN");
		set => SetUserText ("REPLAYGAIN_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetUserText ("REPLAYGAIN_ALBUM_PEAK");
		set => SetUserText ("REPLAYGAIN_ALBUM_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetUserText ("MUSICBRAINZ_TRACKID");
		set => SetUserText ("MUSICBRAINZ_TRACKID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetUserText ("MUSICBRAINZ_ALBUMID");
		set => SetUserText ("MUSICBRAINZ_ALBUMID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetUserText ("MUSICBRAINZ_ARTISTID");
		set => SetUserText ("MUSICBRAINZ_ARTISTID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetUserText ("MUSICBRAINZ_RELEASEGROUPID");
		set => SetUserText ("MUSICBRAINZ_RELEASEGROUPID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetUserText ("MUSICBRAINZ_ALBUMARTISTID");
		set => SetUserText ("MUSICBRAINZ_ALBUMARTISTID", value);
	}

	/// <summary>
	/// Gets or sets the MusicBrainz Recording ID from a UFID frame.
	/// </summary>
	/// <remarks>
	/// This is the canonical way to store a MusicBrainz recording ID in ID3v2.
	/// The UFID frame uses "http://musicbrainz.org" as the owner identifier.
	/// </remarks>
	public string? MusicBrainzRecordingId {
		get => GetUniqueFileId (UniqueFileIdFrame.MusicBrainzOwner)?.IdentifierString;
		set {
			RemoveUniqueFileIds (UniqueFileIdFrame.MusicBrainzOwner);
			if (!string.IsNullOrEmpty (value))
				_uniqueFileIdFrames.Add (new UniqueFileIdFrame (UniqueFileIdFrame.MusicBrainzOwner, value!));
		}
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

		// Limit remaining to actual available data to handle truncated files gracefully.
		// The header may claim a larger size than what's actually present in the data.
		var availableData = data.Length - Id3v2Header.HeaderSize;
		var remaining = (int)Math.Min (header.TagSize, (uint)availableData);

		// Skip extended header if present
		if (header.HasExtendedHeader && remaining >= 4) {
			var extHeaderSize = GetExtendedHeaderSize (frameData, header.MajorVersion);
			if (extHeaderSize > 0 && extHeaderSize <= remaining) {
				frameData = frameData.Slice (extHeaderSize);
				remaining -= extHeaderSize;
			}
		}

		while (remaining >= FrameHeaderSize && frameData.Length >= FrameHeaderSize) {
			// Check for padding (zeros)
			if (frameData[0] == 0)
				break;

			// Read frame header
			var frameId = GetFrameId (frameData);
			if (string.IsNullOrEmpty (frameId))
				break;

			var frameSize = GetFrameSize (frameData.Slice (4, 4), header.MajorVersion);
			if (frameSize <= 0 ||
				frameSize > remaining - FrameHeaderSize ||
				frameSize > frameData.Length - FrameHeaderSize)
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
			// Handle comment frames (COMM)
			else if (frameId == "COMM") {
				var commentResult = CommentFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (commentResult.IsSuccess)
					tag._comments.Add (commentResult.Frame!);
			}
			// Handle user-defined text frames (TXXX)
			else if (frameId == "TXXX") {
				var userTextResult = UserTextFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (userTextResult.IsSuccess)
					tag._userTextFrames.Add (userTextResult.Frame!);
			}
			// Handle lyrics frames (USLT)
			else if (frameId == "USLT") {
				var lyricsResult = LyricsFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (lyricsResult.IsSuccess)
					tag._lyricsFrames.Add (lyricsResult.Frame!);
			}
			// Handle unique file identifier frames (UFID)
			else if (frameId == "UFID") {
				var ufidResult = UniqueFileIdFrame.Read (frameContent, (Id3v2Version)header.MajorVersion);
				if (ufidResult.IsSuccess)
					tag._uniqueFileIdFrames.Add (ufidResult.Frame!);
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
	/// Gets all values from a text frame (multi-value support).
	/// </summary>
	/// <param name="frameId">The frame ID (e.g., TPE1 for artists).</param>
	/// <returns>A list of values, split by null characters (v2.4) or "/" (v2.3).</returns>
	/// <remarks>
	/// ID3v2.4 uses null characters as value separators. ID3v2.3 has no official
	/// separator, but "/" is commonly used. This method handles both.
	/// </remarks>
	public IReadOnlyList<string> GetTextFrameValues (string frameId)
	{
		var text = GetTextFrame (frameId);
		if (string.IsNullOrEmpty (text))
			return Array.Empty<string> ();

		// ID3v2.4 uses null as separator
#if NETSTANDARD2_0
		if (text!.IndexOf ('\0') >= 0) {
#else
		if (text!.Contains ('\0', StringComparison.Ordinal)) {
#endif
			var parts = text.Split ('\0');
			var result = new List<string> (parts.Length);
			for (var i = 0; i < parts.Length; i++) {
				var trimmed = parts[i].Trim ();
				if (!string.IsNullOrEmpty (trimmed))
					result.Add (trimmed);
			}
			return result;
		}

		// ID3v2.3 commonly uses "/" as separator (not official but widely used)
		// Only split if there are multiple values indicated by "/"
#if NETSTANDARD2_0
		if (text.IndexOf ('/') >= 0) {
#else
		if (text.Contains ('/', StringComparison.Ordinal)) {
#endif
			var parts = text.Split ('/');
			var result = new List<string> (parts.Length);
			for (var i = 0; i < parts.Length; i++) {
				var trimmed = parts[i].Trim ();
				if (!string.IsNullOrEmpty (trimmed))
					result.Add (trimmed);
			}
			return result;
		}

		// Single value
		return new List<string> { text };
	}

	/// <summary>
	/// Sets a text frame with multiple values.
	/// </summary>
	/// <param name="frameId">The frame ID.</param>
	/// <param name="values">The values to set.</param>
	/// <remarks>
	/// Values are joined with null characters for ID3v2.4 compatibility.
	/// </remarks>
	public void SetTextFrameValues (string frameId, IEnumerable<string>? values)
	{
		if (values is null) {
			SetTextFrame (frameId, null);
			return;
		}

		var list = new List<string> ();
		foreach (var v in values) {
			if (!string.IsNullOrEmpty (v))
				list.Add (v);
		}

		if (list.Count == 0) {
			SetTextFrame (frameId, null);
			return;
		}

		// Join with null for ID3v2.4 multi-value support
		var joined = string.Join ("\0", list);
		SetTextFrame (frameId, joined);
	}

	/// <summary>
	/// Gets all artist names (multi-value support).
	/// </summary>
	/// <remarks>
	/// Many songs have multiple artists. This property returns all of them.
	/// For a single concatenated string, use <see cref="Artist"/>.
	/// </remarks>
	public IReadOnlyList<string> Artists => GetTextFrameValues ("TPE1");

	/// <summary>
	/// Gets all genre names (multi-value support).
	/// </summary>
	public IReadOnlyList<string> Genres => GetTextFrameValues ("TCON");

	/// <summary>
	/// Gets all composer names (multi-value support).
	/// </summary>
	public IReadOnlyList<string> Composers => GetTextFrameValues ("TCOM");

	/// <summary>
	/// Gets all album artist names (multi-value support).
	/// </summary>
	public IReadOnlyList<string> AlbumArtists => GetTextFrameValues ("TPE2");

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
		var frameDataList = new List<BinaryData> ((_frames.Count + _pictures.Count + _comments.Count) * 2);
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

		// Render comment frames
		for (var i = 0; i < _comments.Count; i++) {
			var content = _comments[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("COMM", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render user-defined text frames (TXXX)
		for (var i = 0; i < _userTextFrames.Count; i++) {
			var content = _userTextFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("TXXX", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render lyrics frames (USLT)
		for (var i = 0; i < _lyricsFrames.Count; i++) {
			var content = _lyricsFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("USLT", content.Length);
			frameDataList.Add (frameHeader);
			frameDataList.Add (content);
			framesSize += frameHeader.Length + content.Length;
		}

		// Render unique file identifier frames (UFID)
		for (var i = 0; i < _uniqueFileIdFrames.Count; i++) {
			var content = _uniqueFileIdFrames[i].RenderContent ();
			var frameHeader = RenderFrameHeader ("UFID", content.Length);
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
		_comments.Clear ();
		_userTextFrames.Clear ();
		_lyricsFrames.Clear ();
		_uniqueFileIdFrames.Clear ();
	}

	/// <summary>
	/// Gets the first comment with an empty description, or the first comment if none have empty description.
	/// </summary>
	string? GetComment ()
	{
		if (_comments.Count == 0)
			return null;

		// Prefer comments with empty description (default comment)
		for (var i = 0; i < _comments.Count; i++) {
			if (string.IsNullOrEmpty (_comments[i].Description))
				return _comments[i].Text;
		}

		// Fall back to first comment
		return _comments[0].Text;
	}

	/// <summary>
	/// Sets or creates a comment with an empty description.
	/// </summary>
	void SetComment (string? value)
	{
		// Remove existing comments with empty description
		_comments.RemoveAll (c => string.IsNullOrEmpty (c.Description));

		// Add new comment if value is not empty
		if (!string.IsNullOrEmpty (value))
			_comments.Add (new CommentFrame (value!));
	}

	/// <summary>
	/// Adds a comment frame to the tag.
	/// </summary>
	/// <param name="comment">The comment frame to add.</param>
	public void AddComment (CommentFrame comment)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (comment is null)
			throw new ArgumentNullException (nameof (comment));
#else
		ArgumentNullException.ThrowIfNull (comment);
#endif
		_comments.Add (comment);
	}

	/// <summary>
	/// Removes all comments with the specified language and description.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	public void RemoveComments (string? language = null, string? description = null)
	{
		_comments.RemoveAll (c =>
			(language is null || c.Language == language) &&
			(description is null || c.Description == description));
	}

	/// <summary>
	/// Gets the first comment matching the specified criteria.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	/// <returns>The comment frame, or null if not found.</returns>
	public CommentFrame? GetCommentFrame (string? language = null, string? description = null)
	{
		for (var i = 0; i < _comments.Count; i++) {
			var c = _comments[i];
			if ((language is null || c.Language == language) &&
				(description is null || c.Description == description))
				return c;
		}
		return null;
	}

	/// <summary>
	/// Gets the value of a user-defined text frame (TXXX) by description.
	/// </summary>
	/// <param name="description">The description (key) of the TXXX frame.</param>
	/// <returns>The value, or null if not found.</returns>
	public string? GetUserText (string description)
	{
		for (var i = 0; i < _userTextFrames.Count; i++) {
			if (string.Equals (_userTextFrames[i].Description, description, StringComparison.OrdinalIgnoreCase))
				return _userTextFrames[i].Value;
		}
		return null;
	}

	/// <summary>
	/// Sets or removes a user-defined text frame (TXXX) by description.
	/// </summary>
	/// <param name="description">The description (key) of the TXXX frame.</param>
	/// <param name="value">The value to set, or null to remove the frame.</param>
	public void SetUserText (string description, string? value)
	{
		// Remove existing frame with this description
		_userTextFrames.RemoveAll (f =>
			string.Equals (f.Description, description, StringComparison.OrdinalIgnoreCase));

		// Add new frame if value is not empty
		if (!string.IsNullOrEmpty (value))
			_userTextFrames.Add (new UserTextFrame (description, value!, TextEncodingType.Utf8));
	}

	/// <summary>
	/// Adds a user-defined text frame to the tag.
	/// </summary>
	/// <param name="frame">The TXXX frame to add.</param>
	public void AddUserTextFrame (UserTextFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_userTextFrames.Add (frame);
	}

	/// <summary>
	/// Removes all user-defined text frames with the specified description.
	/// </summary>
	/// <param name="description">The description to match (case-insensitive).</param>
	public void RemoveUserTextFrames (string description)
	{
		_userTextFrames.RemoveAll (f =>
			string.Equals (f.Description, description, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Gets the first lyrics with an empty description, or the first lyrics if none have empty description.
	/// </summary>
	string? GetLyrics ()
	{
		if (_lyricsFrames.Count == 0)
			return null;

		// Prefer lyrics with empty description (default lyrics)
		for (var i = 0; i < _lyricsFrames.Count; i++) {
			if (string.IsNullOrEmpty (_lyricsFrames[i].Description))
				return _lyricsFrames[i].Text;
		}

		// Fall back to first lyrics
		return _lyricsFrames[0].Text;
	}

	/// <summary>
	/// Sets or creates lyrics with an empty description.
	/// </summary>
	void SetLyrics (string? value)
	{
		// Remove existing lyrics with empty description
		_lyricsFrames.RemoveAll (l => string.IsNullOrEmpty (l.Description));

		// Add new lyrics if value is not empty
		if (!string.IsNullOrEmpty (value))
			_lyricsFrames.Add (new LyricsFrame (value!));
	}

	/// <summary>
	/// Adds a lyrics frame to the tag.
	/// </summary>
	/// <param name="lyrics">The lyrics frame to add.</param>
	public void AddLyrics (LyricsFrame lyrics)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (lyrics is null)
			throw new ArgumentNullException (nameof (lyrics));
#else
		ArgumentNullException.ThrowIfNull (lyrics);
#endif
		_lyricsFrames.Add (lyrics);
	}

	/// <summary>
	/// Removes all lyrics with the specified language and description.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	public void RemoveLyrics (string? language = null, string? description = null)
	{
		_lyricsFrames.RemoveAll (l =>
			(language is null || l.Language == language) &&
			(description is null || l.Description == description));
	}

	/// <summary>
	/// Gets the first lyrics matching the specified criteria.
	/// </summary>
	/// <param name="language">The language code to match (null matches any).</param>
	/// <param name="description">The description to match (null matches any).</param>
	/// <returns>The lyrics frame, or null if not found.</returns>
	public LyricsFrame? GetLyricsFrame (string? language = null, string? description = null)
	{
		for (var i = 0; i < _lyricsFrames.Count; i++) {
			var l = _lyricsFrames[i];
			if ((language is null || l.Language == language) &&
				(description is null || l.Description == description))
				return l;
		}
		return null;
	}

	/// <summary>
	/// Adds a unique file identifier frame to the tag.
	/// </summary>
	/// <param name="frame">The UFID frame to add.</param>
	public void AddUniqueFileId (UniqueFileIdFrame frame)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (frame is null)
			throw new ArgumentNullException (nameof (frame));
#else
		ArgumentNullException.ThrowIfNull (frame);
#endif
		_uniqueFileIdFrames.Add (frame);
	}

	/// <summary>
	/// Gets the first unique file identifier frame with the specified owner.
	/// </summary>
	/// <param name="owner">The owner identifier to match (case-insensitive).</param>
	/// <returns>The UFID frame, or null if not found.</returns>
	public UniqueFileIdFrame? GetUniqueFileId (string owner)
	{
		for (var i = 0; i < _uniqueFileIdFrames.Count; i++) {
			if (string.Equals (_uniqueFileIdFrames[i].Owner, owner, StringComparison.OrdinalIgnoreCase))
				return _uniqueFileIdFrames[i];
		}
		return null;
	}

	/// <summary>
	/// Removes all unique file identifier frames with the specified owner.
	/// </summary>
	/// <param name="owner">The owner identifier to match (case-insensitive), or null to remove all.</param>
	public void RemoveUniqueFileIds (string? owner = null)
	{
		if (owner is null)
			_uniqueFileIdFrames.Clear ();
		else
			_uniqueFileIdFrames.RemoveAll (f =>
				string.Equals (f.Owner, owner, StringComparison.OrdinalIgnoreCase));
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
