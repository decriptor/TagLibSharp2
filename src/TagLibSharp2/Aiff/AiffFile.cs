// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Aiff;

/// <summary>
/// Parses AIFF (Audio Interchange File Format) and AIFC files.
/// </summary>
/// <remarks>
/// AIFF file structure:
/// - Bytes 0-3:   "FORM" magic
/// - Bytes 4-7:   File size - 8 (32-bit big-endian)
/// - Bytes 8-11:  Form type ("AIFF" or "AIFC")
/// - Bytes 12+:   Chunks
///
/// Required chunks: COMM (audio properties), SSND (sound data)
/// Optional chunks: ID3, ANNO, COMT, NAME, AUTH, "(c) ", MARK, INST.
/// Native text is read as Mac OS Roman, or as UTF-8 when the whole value is valid
/// UTF-8 with a multibyte sequence. ID3 is not filled from it on read. Comment,
/// Title, Artist, and Copyright prefer a non-empty ID3 value and otherwise use the
/// native text. A COMT entry with marker id 0 is the file comment and ranks above
/// ANNO. Any other marker id is not the file comment.
///
/// ID3 is the only write target. Setting Comment, Title, Artist, Copyright, or
/// Performers, even to the value it already has, writes the ID3 tag and drops that
/// field's native chunks on save, so a reader that only looks at NAME, AUTH, ANNO,
/// or COMT no longer sees the edited field. Native chunks are never rewritten. When
/// a comment edit drops a COMT file comment, notes for other markers are written back
/// in a COMT chunk with their bytes unchanged. Fields that are not edited keep their
/// native chunks.
///
/// Unlike RIFF/WAV, AIFF uses big-endian byte order throughout.
/// </remarks>
public sealed class AiffFile : IMediaFile
{
	bool _disposed;
	IFileSystem? _sourceFileSystem;

	/// <summary>
	/// Size of the FORM header (FORM + size + form type).
	/// </summary>
	public const int HeaderSize = 12;

	/// <summary>
	/// FORM magic bytes.
	/// </summary>
	public static readonly BinaryData FormMagic = BinaryData.FromStringLatin1 ("FORM");

	/// <summary>
	/// AIFF form type.
	/// </summary>
	public static readonly BinaryData AiffType = BinaryData.FromStringLatin1 ("AIFF");

	/// <summary>
	/// AIFC (compressed AIFF) form type.
	/// </summary>
	public static readonly BinaryData AifcType = BinaryData.FromStringLatin1 ("AIFC");

	/// <summary>
	/// Gets whether this file was successfully parsed.
	/// </summary>
	public bool IsValid { get; private set; }

	/// <summary>
	/// Gets the form type ("AIFF" or "AIFC").
	/// </summary>
	public string FormType { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the file size as stored in the FORM header.
	/// </summary>
	public uint FileSize { get; private set; }

	/// <summary>
	/// Gets the audio properties from the COMM chunk.
	/// </summary>
	public AiffAudioProperties? Properties { get; private set; }

	/// <summary>
	/// Gets or sets the ID3v2 tag stored in the ID3 chunk.
	/// </summary>
	/// <remarks>
	/// Native ANNO, COMT, NAME, AUTH, and copyright text is not copied here.
	/// Null when the file has no ID3 chunk. Save writes this tag when it has any frame.
	/// </remarks>
	public Id3v2Tag? Id3v2Tag {
		get => _id3;
		set => _id3 = value;
	}

	/// <summary>
	/// Gets the combined metadata view.
	/// </summary>
	/// <remarks>
	/// Comment, Title, Artist, and Copyright return the ID3 value when that field
	/// is non-empty, otherwise the native chunk. Other fields come from the ID3 tag.
	/// Every set writes the ID3 tag; see the class remarks. The view is cached. Clearing the last field does not make this null.
	/// Assign an <see cref="Id3v2Tag"/> through <see cref="Id3v2Tag"/>; this property
	/// has no setter, so the combined view cannot be downcast and drop the ID3 chunk.
	/// </remarks>
	public Tag? Tag {
		get {
			if (_metadata is not null)
				return _metadata;
			if (_id3 is null && !HasNativeFields)
				return null;
			return _metadata = new AiffTag (this);
		}
	}

	/// <summary>
	/// Gets or sets the comment. An ID3 comment wins when it is non-empty.
	/// </summary>
	public string? Comment {
		get => Prefer (Id3v2Tag?.Comment, NativeComment);
		set => SetComment (value);
	}

	/// <summary>
	/// Gets or sets the title. An ID3 title wins when it is non-empty.
	/// </summary>
	public string? Title {
		get => Prefer (Id3v2Tag?.Title, _name);
		set => SetTitle (value);
	}

	/// <summary>
	/// Gets or sets the artist. An ID3 artist wins when it is non-empty.
	/// </summary>
	public string? Artist {
		get => Prefer (Id3v2Tag?.Artist, _author);
		set => SetArtist (value);
	}

	/// <summary>
	/// Gets or sets the copyright. An ID3 copyright wins when it is non-empty.
	/// </summary>
	public string? Copyright {
		get => Prefer (Id3v2Tag?.Copyright, _copyright);
		set => SetCopyright (value);
	}

	/// <summary>
	/// Gets a value indicating whether this file has embedded pictures.
	/// </summary>
	public bool HasPictures => Id3v2Tag?.HasPictures ?? false;

	/// <summary>
	/// Gets the embedded pictures from the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// Pictures are only available when an ID3v2 tag is present.
	/// Returns an empty array if no ID3v2 tag exists.
	/// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays - API compatibility
	public IPicture[] Pictures => Id3v2Tag?.Pictures ?? [];
#pragma warning restore CA1819

	/// <summary>
	/// Gets the front cover art from the ID3v2 tag.
	/// </summary>
	/// <remarks>
	/// Returns the first picture with type <see cref="PictureType.FrontCover"/>,
	/// or null if no front cover exists or no ID3v2 tag is present.
	/// </remarks>
	public PictureFrame? CoverArt => Id3v2Tag?.CoverArt;

	/// <summary>
	/// Gets all parsed chunks in order.
	/// </summary>
	public IReadOnlyList<AiffChunk> AllChunks => _chunks;

	/// <summary>
	/// Gets the source file path (not tracked for AIFF files parsed from data).
	/// </summary>
	public string? SourcePath { get; private set; }

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties is not null ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Aiff;

	// '(', 'c', ')', ' ' — the trailing space is part of the FourCC.
	const string CopyrightChunkId = "(c) ";

	readonly List<AiffChunk> _chunks = [];
	readonly List<SkippedChunk> _skipped = [];
	readonly List<MarkerNote> _markers = [];
	readonly List<string> _annotations = [];
	readonly List<string> _markerComments = [];
	Id3v2Tag? _id3;
	AiffTag? _metadata;
	string? _name;
	string? _author;
	string? _copyright;
	NativeCommentKind _commentKind;
	// Set when the field was edited: its native chunks are dropped on save.
	bool _commentEdited;
	bool _nameEdited;
	bool _authorEdited;
	bool _copyrightEdited;

	enum NativeCommentKind
	{
		None,
		Annotation,
		MarkerComment
	}

	// A COMT note for a marker other than 0. Its text bytes are kept as read, because
	// a note is never edited and its encoding is not known for certain.
	readonly struct MarkerNote
	{
		public MarkerNote (uint timestamp, ushort markerId, byte[] raw)
		{
			Timestamp = timestamp;
			MarkerId = markerId;
			Raw = raw;
		}

		public uint Timestamp { get; }

		public ushort MarkerId { get; }

		public byte[] Raw { get; }
	}

	readonly struct SkippedChunk
	{
		public SkippedChunk (int index, long offset, uint presentBytes)
		{
			Index = index;
			Offset = offset;
			PresentBytes = presentBytes;
		}

		public int Index { get; }

		public long Offset { get; }

		public uint PresentBytes { get; }
	}

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	public void Dispose ()
	{
		if (_disposed)
			return;

		_chunks.Clear ();
		_markers.Clear ();
		_annotations.Clear ();
		_markerComments.Clear ();
		Id3v2Tag = null;
		_metadata = null;
		_name = null;
		_author = null;
		_copyright = null;
		_commentKind = NativeCommentKind.None;
		Properties = null;
		_disposed = true;
	}

	/// <summary>
	/// Gets a chunk by its FourCC, or null if not found.
	/// </summary>
	/// <param name="fourCC">The 4-character chunk identifier.</param>
	/// <returns>The first chunk with the matching FourCC, or null.</returns>
	public AiffChunk? GetChunk (string fourCC)
	{
		foreach (var chunk in _chunks) {
			if (chunk.FourCC == fourCC)
				return chunk;
		}
		return null;
	}

	/// <summary>
	/// Attempts to read an AIFF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out AiffFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read an AIFF file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out AiffFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid AIFF file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with "FORM" and contains "AIFF" or "AIFC" form type.</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data)
	{
		// Need at least 12 bytes: FORM (4) + size (4) + form type (4)
		if (data.Length < 12)
			return false;

		// Check "FORM" magic
		if (data[0] != 'F' || data[1] != 'O' || data[2] != 'R' || data[3] != 'M')
			return false;

		// Check form type at offset 8 - must be "AIFF" or "AIFC"
		var isAiff = data[8] == 'A' && data[9] == 'I' && data[10] == 'F' && data[11] == 'F';
		var isAifc = data[8] == 'A' && data[9] == 'I' && data[10] == 'F' && data[11] == 'C';
		return isAiff || isAifc;
	}

	/// <summary>
	/// Reads an AIFF file from binary data with detailed error information.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static AiffFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < HeaderSize)
			return AiffFileReadResult.Failure ("Invalid AIFF file: data too short for header");

		// Check FORM magic
		if (data[0] != 'F' || data[1] != 'O' || data[2] != 'R' || data[3] != 'M')
			return AiffFileReadResult.Failure ("Invalid AIFF file: missing magic (expected 'FORM')");

		// Read form type
		var formType = new BinaryData (data.Slice (8, 4).ToArray ()).ToStringLatin1 ();

		// Validate form type
		if (formType != "AIFF" && formType != "AIFC")
			return AiffFileReadResult.Failure ($"Invalid AIFF file: invalid form type (expected 'AIFF' or 'AIFC', got '{formType}')");

		// Use internal parsing from here
		var file = new AiffFile ();
		if (!file.Parse (new BinaryData (data.ToArray ()))) {
			file.Dispose ();
			return AiffFileReadResult.Failure ("Invalid AIFF file: failed to parse file structure");
		}

		return AiffFileReadResult.Success (file);
	}

	/// <summary>
	/// Reads an AIFF file from a stream. SSND sample bytes stay in the file.
	/// </summary>
	internal static AiffFileReadResult Read (Stream stream, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return Read (StreamBytes.ReadToEnd (stream));

		if (stream.Position != 0)
			stream.Position = 0;

		var header = StreamBytes.ReadAtMost (stream, HeaderSize);
		if (header.Length < HeaderSize || header[0] != (byte)'F' || header[1] != (byte)'O' || header[2] != (byte)'R' || header[3] != (byte)'M')
			return AiffFileReadResult.Failure ("Invalid AIFF file: missing magic (expected 'FORM')");

		AiffFile? file = new () {
			FileSize = ReadUInt32BE (header, 4),
			FormType = FourCC (header, 8)
		};
		if (file.FormType != "AIFF" && file.FormType != "AIFC") {
			var form = file.FormType;
			file.Dispose ();
			return AiffFileReadResult.Failure ($"Invalid AIFF file: invalid form type (expected 'AIFF' or 'AIFC', got '{form}')");
		}

		file.IsValid = true;
		var limit = file.FileSize == 0xFFFFFFFF
			? stream.Length
			: Math.Min (stream.Length, 8L + file.FileSize);

		while (stream.Position + 8 <= limit) {
			var chunkHeader = StreamBytes.ReadAtMost (stream, 8);
			if (chunkHeader.Length < 8)
				break;

			var id = FourCC (chunkHeader, 0);
			var size = ReadUInt32BE (chunkHeader, 4);
			var dataPos = stream.Position;
			var available = limit - dataPos;
			if (available < 0)
				available = 0;
			var present = available < size ? (uint)available : size;
			var pad = (size & 1) == 1 && present == size ? 1 : 0;
			if (id == "SSND" || size > int.MaxValue) {
				if (present > 0 && present <= int.MaxValue)
					file._skipped.Add (new SkippedChunk (file._chunks.Count, dataPos, present));
				file._chunks.Add (AiffChunk.WithDeclaredSize (id, size, BinaryData.Empty));
				var skip = (long)present + pad;
				var room = stream.Length - stream.Position;
				if (skip > room)
					skip = room < 0 ? 0 : room;
				stream.Position += skip;
				if (present < size)
					break;
				continue;
			}

			var data = StreamBytes.ReadAtMost (stream, (int)present);
			if (pad == 1 && stream.Position < limit)
				StreamBytes.ReadAtMost (stream, 1);
			var chunk = present == size
				? new AiffChunk (id, new BinaryData (data))
				: AiffChunk.WithDeclaredSize (id, size, new BinaryData (data));
			file._chunks.Add (chunk);
			file.ProcessChunk (chunk);
			if (present < size)
				break;
		}

		file.FinishNative ();
		file.SourcePath = sourcePath;
		file._sourceFileSystem = fileSystem;
		var parsed = file;
		file = null;
		return AiffFileReadResult.Success (parsed);
	}

	static uint ReadUInt32BE (byte[] data, int offset) =>
		((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

	static string FourCC (byte[] data, int offset) =>
		new ([(char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3]]);

	/// <summary>
	/// Reads an AIFF file from a file path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static AiffFileReadResult ReadFromFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = FileHelper.SafeReadAllBytes (path, fs);
		if (!readResult.IsSuccess)
			return AiffFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	/// <summary>
	/// Reads an AIFF file from a file path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result containing the parsed file or error information.</returns>
	public static async Task<AiffFileReadResult> ReadFromFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? DefaultFileSystem.Instance;
		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fs, cancellationToken)
			.ConfigureAwait (false);

		if (!readResult.IsSuccess)
			return AiffFileReadResult.Failure (readResult.Error!);

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fs;
		}
		return result;
	}

	bool Parse (BinaryData data)
	{
		if (data.Length < HeaderSize)
			return false;

		var span = data.Span;

		// Check FORM magic
		if (span[0] != 'F' || span[1] != 'O' || span[2] != 'R' || span[3] != 'M')
			return false;

		// Read file size (big-endian)
		FileSize = (uint)(
			(span[4] << 24) |
			(span[5] << 16) |
			(span[6] << 8) |
			span[7]);

		// Read form type
		FormType = data.Slice (8, 4).ToStringLatin1 ();

		// Validate form type
		if (FormType != "AIFF" && FormType != "AIFC")
			return false;

		IsValid = true;

		// Parse chunks. TotalSize is a long so a size near int.MaxValue does not walk backward.
		long offset = HeaderSize;
		while (offset < data.Length) {
			if (offset > int.MaxValue || !AiffChunk.TryParse (data, (int)offset, out var chunk))
				break;

			_chunks.Add (chunk!);
			var next = offset + chunk!.TotalSize;
			if (next <= offset)
				break;

			offset = next;

			// Process specific chunk types
			ProcessChunk (chunk);
		}

		FinishNative ();
		return true;
	}

	void ProcessChunk (AiffChunk chunk)
	{
		switch (chunk.FourCC) {
			case "COMM":
				ParseCommChunk (chunk);
				break;
			case "ID3 ":
			case "ID3":
				ParseId3Chunk (chunk);
				break;
			case "ANNO":
				// A file may carry more than one annotation. Keep each of them.
				AddText (_annotations, MacRoman.Decode (chunk.Data.Span));
				break;
			case "COMT":
				ReadComments (chunk.Data);
				break;
			case "NAME":
				_name ??= MacRoman.Decode (chunk.Data.Span);
				break;
			case "AUTH":
				_author ??= MacRoman.Decode (chunk.Data.Span);
				break;
			case CopyrightChunkId:
				_copyright ??= MacRoman.Decode (chunk.Data.Span);
				break;
		}
	}

	// Marker id 0 is the file comment and ranks above ANNO. Other marker ids stay out of Comment.
	void FinishNative ()
	{
		if (_markerComments.Count > 0)
			_commentKind = NativeCommentKind.MarkerComment;
		else if (_annotations.Count > 0)
			_commentKind = NativeCommentKind.Annotation;
		else
			_commentKind = NativeCommentKind.None;

		if (_id3 is not null || HasNativeFields)
			_metadata ??= new AiffTag (this);
	}

	internal bool HasNativeFields =>
		_annotations.Count > 0
		|| _markerComments.Count > 0
		|| _name is not null
		|| _author is not null
		|| _copyright is not null;

	internal bool HasNativeSource => HasNativeFields || _markers.Count > 0;

	string? NativeComment => _commentKind switch {
		NativeCommentKind.MarkerComment => Join (_markerComments),
		NativeCommentKind.Annotation => Join (_annotations),
		_ => null
	};

	static string? Prefer (string? primary, string? fallback) =>
		string.IsNullOrEmpty (primary) ? fallback : primary;

	void SetComment (string? value)
	{
		// ID3 can hold a comment, including one longer than a COMT text field.
		_commentEdited = true;
		_annotations.Clear ();
		_markerComments.Clear ();
		_commentKind = NativeCommentKind.None;
		if (string.IsNullOrEmpty (value)) {
			if (Id3v2Tag is not null)
				Id3v2Tag.Comment = null;
			return;
		}

		EnsureId3 ().Comment = value;
	}

	void SetTitle (string? value) =>
		SetId3Field (value, ref _nameEdited, ref _name, static (tag, text) => tag.Title = text);

	void SetArtist (string? value) =>
		SetId3Field (value, ref _authorEdited, ref _author, static (tag, text) => tag.Artist = text);

	void SetCopyright (string? value) =>
		SetId3Field (value, ref _copyrightEdited, ref _copyright, static (tag, text) => tag.Copyright = text);

	void SetId3Field (string? value, ref bool edited, ref string? native, Action<Id3v2Tag, string?> write)
	{
		edited = true;
		native = null;
		if (string.IsNullOrEmpty (value)) {
			if (Id3v2Tag is not null)
				write (Id3v2Tag, null);
			return;
		}

		write (EnsureId3 (), value);
	}

	internal void SetPerformers (string[]? value)
	{
		if (!HasText (value)) {
			SetArtist (null);
			return;
		}

		// Same TPE1 frame as Artist. Drop AUTH so an empty performers list cannot
		// leave the old author behind, and a new list cannot shadow it.
		_authorEdited = true;
		_author = null;
		EnsureId3 ().Performers = value!;
	}

	static bool HasText (string[]? value)
	{
		if (value is null)
			return false;
		for (var i = 0; i < value.Length; i++) {
			if (!string.IsNullOrEmpty (value[i]))
				return true;
		}

		return false;
	}

	internal Id3v2Tag EnsureId3 () => Id3v2Tag ??= new Id3v2Tag ();

	internal void ClearMappedFields ()
	{
		SetComment (null);
		SetTitle (null);
		SetArtist (null);
		SetCopyright (null);
		Id3v2Tag?.Clear ();
	}

	static void AddText (List<string> parts, string? text)
	{
		if (!string.IsNullOrEmpty (text))
			parts.Add (text!);
	}

	static string? Join (List<string> parts) =>
		parts.Count == 0 ? null : string.Join ("\n", parts);

	void ReadComments (BinaryData data)
	{
		if (data.Length < 2)
			return;

		var count = data.ToUInt16BE ();
		var offset = 2;
		for (var i = 0; i < count; i++) {
			if (data.Length - offset < 8)
				break;

			var timestamp = data.ToUInt32BE (offset);
			var marker = data.ToUInt16BE (offset + 4);
			var textLength = data.ToUInt16BE (offset + 6);
			offset += 8;
			if (data.Length - offset < textLength)
				break;

			string? text = null;
			byte[] raw = [];
			if (textLength > 0) {
				raw = data.Slice (offset, textLength).ToArray ();
				text = MacRoman.Decode (raw);
			}

			offset += textLength;
			// Odd-length text is padded inside ckSize. The pad is not part of the text.
			if ((textLength & 1) == 1 && offset < data.Length)
				offset++;

			if (string.IsNullOrEmpty (text))
				continue;

			if (marker == 0)
				_markerComments.Add (text!);
			else
				_markers.Add (new MarkerNote (timestamp, marker, raw));
		}
	}

	void ParseCommChunk (AiffChunk chunk)
	{
		if (AiffAudioProperties.TryParse (chunk.Data, out var props))
			Properties = props;
	}

	void ParseId3Chunk (AiffChunk chunk)
	{
		// The ID3 chunk contains a complete ID3v2 tag. Native text is not copied into it.
		var result = Id3v2Tag.Read (chunk.Data.Span);
		if (result.IsSuccess)
			Id3v2Tag = result.Tag;
	}

	/// <summary>
	/// Renders the AIFF file to binary data.
	/// </summary>
	/// <returns>The complete AIFF file as binary data.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the file is not valid.</exception>
	public BinaryData Render ()
	{
		if (!IsValid)
			throw new InvalidOperationException ("Cannot render invalid AIFF file");

		if (!TryLoadSkipped (null, out var skipped))
			throw new InvalidOperationException (skipped);

		var output = new List<AiffChunk> (_chunks.Count + 4);
		foreach (var chunk in _chunks) {
			if (IsId3Chunk (chunk.FourCC) || OmitNativeChunk (chunk.FourCC))
				continue;
			output.Add (chunk);
		}

		AppendMarkerNotes (output);
		if (Id3v2Tag is not null && !Id3v2Tag.IsEmpty) {
			var id3Data = Id3v2Tag.Render ();
			if (id3Data.Length > 0)
				output.Add (new AiffChunk ("ID3 ", id3Data));
		}

		// TotalSize is a long file length, not a wrapped int.
		long chunksSize = 0;
		foreach (var chunk in output)
			chunksSize += chunk.TotalSize;

		if (chunksSize > int.MaxValue - HeaderSize)
			throw new InvalidOperationException ("Cannot render an AIFF file larger than supported memory.");

		var contentSize = 4 + chunksSize;
		var totalSize = (int)(HeaderSize + chunksSize);

		using var builder = new BinaryDataBuilder (totalSize);
		builder.AddStringLatin1 ("FORM");
		builder.AddUInt32BE ((uint)contentSize);
		builder.AddStringLatin1 (FormType);
		foreach (var chunk in output)
			builder.Add (chunk.Render ());

		return builder.ToBinaryData ();
	}

	static bool IsId3Chunk (string fourCC) => fourCC is "ID3 " or "ID3";

	bool OmitNativeChunk (string fourCC)
	{
		if (fourCC is "ANNO" or "COMT")
			return _commentEdited;
		if (fourCC == "NAME")
			return _nameEdited;
		if (fourCC == "AUTH")
			return _authorEdited;
		if (fourCC == CopyrightChunkId)
			return _copyrightEdited;
		return false;
	}

	// An edited comment drops every COMT chunk. Notes for other markers were not
	// edited, so they go back in one COMT chunk (AIFF 1.3 section 13).
	void AppendMarkerNotes (List<AiffChunk> output)
	{
		if (_commentEdited && _markers.Count > 0)
			output.Add (new AiffChunk ("COMT", RenderComments (_markers)));
	}

	static BinaryData RenderComments (List<MarkerNote> notes)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt16BE ((ushort)notes.Count);
		foreach (var note in notes) {
			body.AddUInt32BE (note.Timestamp);
			body.AddUInt16BE (note.MarkerId);
			// Raw came from a 16-bit count, so it always fits.
			body.AddUInt16BE ((ushort)note.Raw.Length);
			body.Add (note.Raw);
			// Odd-length text is padded inside ckSize. The pad is not part of the text.
			if ((note.Raw.Length & 1) == 1)
				body.Add (0);
		}

		return body.ToBinaryData ();
	}

	/// <summary>
	/// Saves the AIFF file to the specified path.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <returns>The write result.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		var fs = fileSystem ?? _sourceFileSystem;
		if (!TryLoadSkipped (fs, out var skipped))
			return FileWriteResult.Failure (skipped!);

		var data = Render ();
		return AtomicFileWriter.Write (path, data.Span, fs);
	}

	bool TryLoadSkipped (IFileSystem? fileSystem, out string? error)
	{
		error = null;
		if (_skipped.Count == 0)
			return true;

		foreach (var chunk in _skipped) {
			if (chunk.PresentBytes > int.MaxValue) {
				error = "Cannot save an AIFF file whose sound data does not fit in memory.";
				return false;
			}
		}

		if (SourcePath is not { Length: > 0 } path) {
			error = "Cannot save an AIFF file whose sound data was not loaded.";
			return false;
		}

		var fs = fileSystem ?? _sourceFileSystem ?? DefaultFileSystem.Instance;
		using var stream = fs.OpenRead (path);
		foreach (var chunk in _skipped) {
			if (stream.Length < chunk.Offset + chunk.PresentBytes) {
				error = "Cannot save an AIFF file whose sound data is no longer in the source file.";
				return false;
			}
		}

		foreach (var chunk in _skipped) {
			stream.Position = chunk.Offset;
			var samples = StreamBytes.ReadAtMost (stream, (int)chunk.PresentBytes);
			var existing = _chunks[chunk.Index];
			_chunks[chunk.Index] = AiffChunk.WithDeclaredSize (existing.FourCC, existing.Size, new BinaryData (samples));
		}

		_skipped.Clear ();
		return true;
	}

	/// <summary>
	/// Saves the AIFF file to the specified path asynchronously.
	/// </summary>
	/// <param name="path">The file path.</param>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var fs = fileSystem ?? _sourceFileSystem;
		if (!TryLoadSkipped (fs, out var skipped))
			return Task.FromResult (FileWriteResult.Failure (skipped!));

		var data = Render ();
		return AtomicFileWriter.WriteAsync (path, data.Memory, fs, cancellationToken);
	}

	/// <summary>
	/// Saves the AIFF file back to its source path asynchronously.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>The write result.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		var fs = fileSystem ?? _sourceFileSystem;
		return SaveToFileAsync (SourcePath!, fs, cancellationToken);
	}
}

/// <summary>
/// Represents the result of reading an AIFF file.
/// </summary>
public readonly struct AiffFileReadResult : IEquatable<AiffFileReadResult>
{
	/// <summary>
	/// Gets the parsed AIFF file, or null if parsing failed.
	/// </summary>
	public AiffFile? File { get; }

	/// <summary>
	/// Gets the error message if parsing failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets whether the parsing was successful.
	/// </summary>
	public bool IsSuccess => File is not null;

	AiffFileReadResult (AiffFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static AiffFileReadResult Success (AiffFile file) => new (file, null);

	/// <summary>
	/// Creates a failure result.
	/// </summary>
	public static AiffFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (AiffFileReadResult other) =>
		ReferenceEquals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is AiffFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () =>
		HashCode.Combine (File, Error);

	/// <summary>
	/// Determines whether two results are equal.
	/// </summary>
	public static bool operator == (AiffFileReadResult left, AiffFileReadResult right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two results are not equal.
	/// </summary>
	public static bool operator != (AiffFileReadResult left, AiffFileReadResult right) =>
		!left.Equals (right);
}
