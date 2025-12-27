// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Xiph;

/// <summary>
/// Represents a Vorbis Comment metadata block used by FLAC and Ogg Vorbis files.
/// </summary>
/// <remarks>
/// <para>
/// Vorbis Comments provide a flexible key-value metadata format with these features:
/// </para>
/// <list type="bullet">
/// <item>Case-insensitive field names (stored uppercase by convention)</item>
/// <item>UTF-8 encoded values</item>
/// <item>Multiple values per field name (e.g., multiple ARTIST entries)</item>
/// <item>Custom field support</item>
/// </list>
/// <para>
/// Standard field mappings:
/// </para>
/// <list type="bullet">
/// <item>Title → TITLE</item>
/// <item>Artist → ARTIST</item>
/// <item>Album → ALBUM</item>
/// <item>Year → DATE</item>
/// <item>Genre → GENRE</item>
/// <item>Track → TRACKNUMBER</item>
/// <item>Comment → COMMENT</item>
/// </list>
/// <para>
/// Reference: https://xiph.org/vorbis/doc/v-comment.html
/// </para>
/// </remarks>
public sealed class VorbisComment : Tag
{
	const string MetadataBlockPictureField = "METADATA_BLOCK_PICTURE";
	readonly List<VorbisCommentField> _fields = new (16);
	readonly List<FlacPicture> _pictures = new (2);

	/// <summary>
	/// Gets or sets the vendor string identifying the encoder/tagger.
	/// </summary>
	public string VendorString { get; set; }

	/// <summary>
	/// Gets the list of all fields in this comment block.
	/// </summary>
	public IReadOnlyList<VorbisCommentField> Fields => _fields;

	/// <summary>
	/// Gets the list of embedded pictures.
	/// </summary>
	/// <remarks>
	/// For Ogg Vorbis, pictures are stored as base64-encoded METADATA_BLOCK_PICTURE fields.
	/// For FLAC, pictures are stored separately in PICTURE metadata blocks (use FlacFile.Pictures instead).
	/// </remarks>
	public IReadOnlyList<FlacPicture> Pictures => _pictures;

	/// <inheritdoc/>
	public override string? Title {
		get => GetValue ("TITLE");
		set => SetValue ("TITLE", value);
	}

	/// <inheritdoc/>
	public override string? Artist {
		get => GetValue ("ARTIST");
		set => SetValue ("ARTIST", value);
	}

	/// <inheritdoc/>
	public override string? Album {
		get => GetValue ("ALBUM");
		set => SetValue ("ALBUM", value);
	}

	/// <inheritdoc/>
	public override string? Year {
		get => GetValue ("DATE");
		set => SetValue ("DATE", value);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Uses ORIGINALDATE field. Falls back to ORIGINALYEAR if ORIGINALDATE is not found.
	/// </remarks>
	public override string? OriginalReleaseDate {
		get => GetValue ("ORIGINALDATE") ?? GetValue ("ORIGINALYEAR");
		set => SetValue ("ORIGINALDATE", value);
	}

	/// <inheritdoc/>
	public override string? Genre {
		get => GetValue ("GENRE");
		set => SetValue ("GENRE", value);
	}

	/// <inheritdoc/>
	public override uint? Track {
		get {
			var value = GetValue ("TRACKNUMBER");
			if (string.IsNullOrEmpty (value))
				return null;

			// Handle "5/12" format
#if NETSTANDARD2_0
			var slashIndex = value!.IndexOf ('/');
#else
			var slashIndex = value!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0)
				value = value.Substring (0, slashIndex);

			return uint.TryParse (value, out var track) ? track : null;
		}
		set => SetValue ("TRACKNUMBER", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Comment {
		get => GetValue ("COMMENT");
		set => SetValue ("COMMENT", value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtist {
		get => GetValue ("ALBUMARTIST");
		set => SetValue ("ALBUMARTIST", value);
	}

	/// <inheritdoc/>
	public override uint? DiscNumber {
		get {
			var value = GetValue ("DISCNUMBER");
			if (string.IsNullOrEmpty (value))
				return null;

			// Handle "2/3" format
#if NETSTANDARD2_0
			var slashIndex = value!.IndexOf ('/');
#else
			var slashIndex = value!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0)
				value = value.Substring (0, slashIndex);

			return uint.TryParse (value, out var disc) ? disc : null;
		}
		set => SetValue ("DISCNUMBER", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Composer {
		get => GetValue ("COMPOSER");
		set => SetValue ("COMPOSER", value);
	}

	/// <inheritdoc/>
	public override uint? BeatsPerMinute {
		get {
			var value = GetValue ("BPM");
			if (string.IsNullOrEmpty (value))
				return null;
			return uint.TryParse (value, out var bpm) ? bpm : null;
		}
		set => SetValue ("BPM", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <inheritdoc/>
	public override string? Conductor {
		get => GetValue ("CONDUCTOR");
		set => SetValue ("CONDUCTOR", value);
	}

	/// <inheritdoc/>
	public override string? Copyright {
		get => GetValue ("COPYRIGHT");
		set => SetValue ("COPYRIGHT", value);
	}

	/// <inheritdoc/>
	public override bool IsCompilation {
		get => GetValue ("COMPILATION") == "1";
		set {
			if (value)
				SetValue ("COMPILATION", "1");
			else
				SetValue ("COMPILATION", null);
		}
	}

	/// <inheritdoc/>
	public override string? Isrc {
		get => GetValue ("ISRC");
		set => SetValue ("ISRC", value);
	}

	/// <inheritdoc/>
	public override string? Publisher {
		get => GetValue ("LABEL");
		set => SetValue ("LABEL", value);
	}

	/// <inheritdoc/>
	public override string? AlbumSort {
		get => GetValue ("ALBUMSORT");
		set => SetValue ("ALBUMSORT", value);
	}

	/// <inheritdoc/>
	public override string? ArtistSort {
		get => GetValue ("ARTISTSORT");
		set => SetValue ("ARTISTSORT", value);
	}

	/// <inheritdoc/>
	public override string? TitleSort {
		get => GetValue ("TITLESORT");
		set => SetValue ("TITLESORT", value);
	}

	/// <inheritdoc/>
	public override string? AlbumArtistSort {
		get => GetValue ("ALBUMARTISTSORT");
		set => SetValue ("ALBUMARTISTSORT", value);
	}

	/// <inheritdoc/>
	public override string? Lyrics {
		get => GetValue ("LYRICS");
		set => SetValue ("LYRICS", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackGain {
		get => GetValue ("REPLAYGAIN_TRACK_GAIN");
		set => SetValue ("REPLAYGAIN_TRACK_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainTrackPeak {
		get => GetValue ("REPLAYGAIN_TRACK_PEAK");
		set => SetValue ("REPLAYGAIN_TRACK_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumGain {
		get => GetValue ("REPLAYGAIN_ALBUM_GAIN");
		set => SetValue ("REPLAYGAIN_ALBUM_GAIN", value);
	}

	/// <inheritdoc/>
	public override string? ReplayGainAlbumPeak {
		get => GetValue ("REPLAYGAIN_ALBUM_PEAK");
		set => SetValue ("REPLAYGAIN_ALBUM_PEAK", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzTrackId {
		get => GetValue ("MUSICBRAINZ_TRACKID");
		set => SetValue ("MUSICBRAINZ_TRACKID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseId {
		get => GetValue ("MUSICBRAINZ_ALBUMID");
		set => SetValue ("MUSICBRAINZ_ALBUMID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzArtistId {
		get => GetValue ("MUSICBRAINZ_ARTISTID");
		set => SetValue ("MUSICBRAINZ_ARTISTID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzReleaseGroupId {
		get => GetValue ("MUSICBRAINZ_RELEASEGROUPID");
		set => SetValue ("MUSICBRAINZ_RELEASEGROUPID", value);
	}

	/// <inheritdoc/>
	public override string? MusicBrainzAlbumArtistId {
		get => GetValue ("MUSICBRAINZ_ALBUMARTISTID");
		set => SetValue ("MUSICBRAINZ_ALBUMARTISTID", value);
	}

	/// <summary>
	/// Gets or sets the total number of tracks on the album.
	/// </summary>
	/// <remarks>
	/// Also reads from TRACKNUMBER field if in "5/12" format.
	/// </remarks>
	public override uint? TotalTracks {
		get {
			// First check TOTALTRACKS field
			var totalTracksValue = GetValue ("TOTALTRACKS");
			if (!string.IsNullOrEmpty (totalTracksValue) && uint.TryParse (totalTracksValue, out var total))
				return total;

			// Fall back to parsing TRACKNUMBER "5/12" format
			var trackValue = GetValue ("TRACKNUMBER");
			if (string.IsNullOrEmpty (trackValue))
				return null;

#if NETSTANDARD2_0
			var slashIndex = trackValue!.IndexOf ('/');
#else
			var slashIndex = trackValue!.IndexOf ('/', StringComparison.Ordinal);
#endif
			if (slashIndex > 0) {
				var totalPart = trackValue.Substring (slashIndex + 1);
				if (uint.TryParse (totalPart, out var totalFromTrack))
					return totalFromTrack;
			}

			return null;
		}
		set => SetValue ("TOTALTRACKS", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Gets or sets the total number of discs.
	/// </summary>
	public override uint? TotalDiscs {
		get {
			var value = GetValue ("TOTALDISCS");
			return !string.IsNullOrEmpty (value) && uint.TryParse (value, out var total) ? total : null;
		}
		set => SetValue ("TOTALDISCS", value?.ToString (System.Globalization.CultureInfo.InvariantCulture));
	}

	/// <summary>
	/// Gets all artist values (multi-value support).
	/// </summary>
	public IReadOnlyList<string> Artists => GetValues ("ARTIST");

	/// <summary>
	/// Gets all genre values (multi-value support).
	/// </summary>
	public IReadOnlyList<string> Genres => GetValues ("GENRE");

	/// <summary>
	/// Gets all composer values (multi-value support).
	/// </summary>
	public IReadOnlyList<string> Composers => GetValues ("COMPOSER");

	/// <summary>
	/// Initializes a new instance of the <see cref="VorbisComment"/> class with an empty vendor string.
	/// </summary>
	public VorbisComment () : this ("")
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="VorbisComment"/> class.
	/// </summary>
	/// <param name="vendorString">The vendor string identifying the encoder.</param>
	public VorbisComment (string vendorString)
	{
		VendorString = vendorString ?? "";
	}

	/// <summary>
	/// Adds a field to the comment block.
	/// </summary>
	/// <param name="name">The field name (will be uppercased).</param>
	/// <param name="value">The field value.</param>
	/// <remarks>
	/// This method allows adding multiple fields with the same name,
	/// which is valid in Vorbis Comments (e.g., multiple ARTIST fields).
	/// </remarks>
	public void AddField (string name, string value)
	{
		_fields.Add (new VorbisCommentField (name, value));
	}

	/// <summary>
	/// Gets the first value for a field name.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <returns>The first value, or null if not found.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public string? GetValue (string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
		var upperName = name.ToUpperInvariant ();
		// Use explicit check instead of relying on struct default behavior.
		// When no match is found, FirstOrDefault returns default(VorbisCommentField)
		// which has null properties - but we make this explicit for clarity.
		for (var i = 0; i < _fields.Count; i++) {
			if (_fields[i].Name == upperName)
				return _fields[i].Value;
		}
		return null;
	}

	/// <summary>
	/// Gets all values for a field name.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <returns>A list of all values for the field.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public IReadOnlyList<string> GetValues (string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
		var upperName = name.ToUpperInvariant ();
		// Use for loop instead of LINQ for better performance
		var result = new List<string> ();
		for (var i = 0; i < _fields.Count; i++) {
			if (_fields[i].Name == upperName)
				result.Add (_fields[i].Value);
		}
		return result;
	}

	/// <summary>
	/// Sets a single value for a field, replacing all existing values.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <param name="value">The value, or null to remove all fields with this name.</param>
	public void SetValue (string name, string? value)
	{
		RemoveAll (name);
		if (!string.IsNullOrEmpty (value))
			AddField (name, value!);
	}

	/// <summary>
	/// Removes all fields with the specified name.
	/// </summary>
	/// <param name="name">The field name (case-insensitive).</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public void RemoveAll (string name)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (name is null)
			throw new ArgumentNullException (nameof (name));
#else
		ArgumentNullException.ThrowIfNull (name);
#endif
		var upperName = name.ToUpperInvariant ();
		_fields.RemoveAll (f => f.Name == upperName);
	}

	/// <inheritdoc/>
	public override void Clear ()
	{
		_fields.Clear ();
		_pictures.Clear ();
	}

	/// <summary>
	/// Adds a picture to this comment block.
	/// </summary>
	/// <param name="picture">The picture to add.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="picture"/> is null.</exception>
	/// <remarks>
	/// For Ogg Vorbis, pictures are stored as base64-encoded METADATA_BLOCK_PICTURE fields.
	/// When rendering, each picture will be encoded and added as a field.
	/// </remarks>
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
	/// Removes all pictures from this comment block.
	/// </summary>
	public void RemoveAllPictures ()
	{
		_pictures.Clear ();
	}

	/// <summary>
	/// Attempts to read a Vorbis Comment block from binary data.
	/// </summary>
	/// <param name="data">The binary data (little-endian format).</param>
	/// <returns>A result indicating success or failure.</returns>
	/// <remarks>
	/// <para>
	/// Binary format:
	/// </para>
	/// <code>
	/// [vendor_len:4 LE][vendor:n UTF-8]
	/// [field_count:4 LE]
	/// For each field:
	///   [field_len:4 LE][FIELDNAME=value:n UTF-8]
	/// </code>
	/// </remarks>
	public static VorbisCommentReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < 8)
			return VorbisCommentReadResult.Failure ("Data too short for Vorbis Comment header");

		var offset = 0;

		// Vendor string length (4 bytes, little-endian)
		var vendorLen = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		// Overflow protection: check if length exceeds int.MaxValue
		if (vendorLen > int.MaxValue)
			return VorbisCommentReadResult.Failure ("Vendor string length overflow (exceeds maximum)");

		if (offset + (int)vendorLen > data.Length)
			return VorbisCommentReadResult.Failure ("Invalid vendor string length");

		var vendorString = System.Text.Encoding.UTF8.GetString (data.Slice (offset, (int)vendorLen));
		offset += (int)vendorLen;

		if (offset + 4 > data.Length)
			return VorbisCommentReadResult.Failure ("Data too short for field count");

		// Field count (4 bytes, little-endian)
		var fieldCount = ReadUInt32LE (data.Slice (offset, 4));
		offset += 4;

		var comment = new VorbisComment (vendorString);

		// Read each field
		for (var i = 0; i < fieldCount; i++) {
			if (offset + 4 > data.Length)
				return VorbisCommentReadResult.Failure ($"Data too short for field {i} length");

			var fieldLen = ReadUInt32LE (data.Slice (offset, 4));
			offset += 4;

			// Overflow protection: check if length exceeds int.MaxValue
			if (fieldLen > int.MaxValue)
				return VorbisCommentReadResult.Failure ($"Field {i} length overflow (exceeds maximum)");

			if (offset + (int)fieldLen > data.Length)
				return VorbisCommentReadResult.Failure ($"Invalid field {i} length");

			var fieldString = System.Text.Encoding.UTF8.GetString (data.Slice (offset, (int)fieldLen));
			offset += (int)fieldLen;

			var parseResult = VorbisCommentField.Parse (fieldString);
			if (parseResult.IsSuccess) {
				// Check for METADATA_BLOCK_PICTURE and parse as picture
				if (parseResult.Field.Name == MetadataBlockPictureField) {
					var picture = TryParseMetadataBlockPicture (parseResult.Field.Value);
					if (picture is not null) {
						// Successfully parsed - add to pictures only, not fields
						comment._pictures.Add (picture);
						continue;
					}
					// Failed to parse - preserve in fields so data isn't lost
				}
				comment._fields.Add (parseResult.Field);
			}
			// Skip malformed fields per spec - don't fail the entire block
		}

		return VorbisCommentReadResult.Success (comment, offset);
	}

	static FlacPicture? TryParseMetadataBlockPicture (string base64Value)
	{
		try {
			var pictureData = Convert.FromBase64String (base64Value);
			var result = FlacPicture.Read (pictureData);
			return result.IsSuccess ? result.Picture : null;
		} catch (FormatException) {
			// Invalid base64 - skip this picture
			return null;
		}
	}

	/// <inheritdoc/>
	public override BinaryData Render ()
	{
		// Encode vendor string
		var vendorBytes = System.Text.Encoding.UTF8.GetBytes (VendorString);

		// Single pass: filter fields, encode, and calculate size simultaneously
		// This avoids LINQ overhead and multiple intermediate allocations
		var fieldBytesList = new List<byte[]> (_fields.Count);
		var fieldBytesSize = 0;
		for (var i = 0; i < _fields.Count; i++) {
			var field = _fields[i];
			if (field.Name == MetadataBlockPictureField)
				continue; // Skip - we'll regenerate from _pictures
			var fieldBytes = System.Text.Encoding.UTF8.GetBytes (field.ToString ());
			fieldBytesList.Add (fieldBytes);
			fieldBytesSize += 4 + fieldBytes.Length;
		}

		// Generate METADATA_BLOCK_PICTURE fields for pictures
		var pictureFieldBytesList = new List<byte[]> (_pictures.Count);
		var pictureBytesSize = 0;
		for (var i = 0; i < _pictures.Count; i++) {
			var pictureData = _pictures[i].RenderContent ();
			var base64 = Convert.ToBase64String (pictureData.ToArray ());
			var fieldString = $"{MetadataBlockPictureField}={base64}";
			var fieldBytes = System.Text.Encoding.UTF8.GetBytes (fieldString);
			pictureFieldBytesList.Add (fieldBytes);
			pictureBytesSize += 4 + fieldBytes.Length;
		}

		var totalFieldCount = fieldBytesList.Count + pictureFieldBytesList.Count;
		var totalSize = 4 + vendorBytes.Length + 4 + fieldBytesSize + pictureBytesSize;

		using var builder = new BinaryDataBuilder (totalSize);

		// Vendor string - explicit little-endian (spec requirement)
		WriteUInt32LE (builder, (uint)vendorBytes.Length);
		builder.Add (vendorBytes);

		// Field count - explicit little-endian
		WriteUInt32LE (builder, (uint)totalFieldCount);

		// Regular fields
		for (var i = 0; i < fieldBytesList.Count; i++) {
			var fieldBytes = fieldBytesList[i];
			WriteUInt32LE (builder, (uint)fieldBytes.Length);
			builder.Add (fieldBytes);
		}

		// Picture fields
		for (var i = 0; i < pictureFieldBytesList.Count; i++) {
			var fieldBytes = pictureFieldBytesList[i];
			WriteUInt32LE (builder, (uint)fieldBytes.Length);
			builder.Add (fieldBytes);
		}

		return builder.ToBinaryData ();
	}

	static void WriteUInt32LE (BinaryDataBuilder builder, uint value)
	{
		builder.Add ((byte)(value & 0xFF));
		builder.Add ((byte)((value >> 8) & 0xFF));
		builder.Add ((byte)((value >> 16) & 0xFF));
		builder.Add ((byte)((value >> 24) & 0xFF));
	}

	static uint ReadUInt32LE (ReadOnlySpan<byte> data)
	{
		return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
	}
}

/// <summary>
/// Represents the result of reading a <see cref="VorbisComment"/> from binary data.
/// </summary>
public readonly struct VorbisCommentReadResult : IEquatable<VorbisCommentReadResult>
{
	/// <summary>
	/// Gets the parsed tag, or null if parsing failed.
	/// </summary>
	public VorbisComment? Tag { get; }

	/// <summary>
	/// Gets a value indicating whether parsing was successful.
	/// </summary>
	public bool IsSuccess => Tag is not null && Error is null;

	/// <summary>
	/// Gets the error message if parsing failed, or null if successful.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the number of bytes consumed from the input data.
	/// </summary>
	public int BytesConsumed { get; }

	VorbisCommentReadResult (VorbisComment? tag, string? error, int bytesConsumed)
	{
		Tag = tag;
		Error = error;
		BytesConsumed = bytesConsumed;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	/// <param name="tag">The parsed tag.</param>
	/// <param name="bytesConsumed">The number of bytes consumed.</param>
	/// <returns>A successful result.</returns>
	public static VorbisCommentReadResult Success (VorbisComment tag, int bytesConsumed) =>
		new (tag, null, bytesConsumed);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	/// <param name="error">The error message.</param>
	/// <returns>A failure result.</returns>
	public static VorbisCommentReadResult Failure (string error) =>
		new (null, error, 0);

	/// <inheritdoc/>
	public bool Equals (VorbisCommentReadResult other) =>
		ReferenceEquals (Tag, other.Tag) &&
		Error == other.Error &&
		BytesConsumed == other.BytesConsumed;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is VorbisCommentReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (Tag, Error, BytesConsumed);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (VorbisCommentReadResult left, VorbisCommentReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (VorbisCommentReadResult left, VorbisCommentReadResult right) =>
		!left.Equals (right);
}
