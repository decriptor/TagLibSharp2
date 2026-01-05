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
public sealed class FlacFile : IMediaFile
{
	const int MagicSize = 4;
	static readonly byte[] FlacMagic = [(byte)'f', (byte)'L', (byte)'a', (byte)'C'];

	readonly List<FlacPicture> _pictures = new (2);
	readonly List<FlacPreservedBlock> _preservedBlocks = new (2);
	bool _disposed;

	/// <summary>
	/// Gets the source file path if the file was read from disk.
	/// </summary>
	/// <remarks>
	/// This is set when using <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// It is null when the file was read from binary data using <see cref="Read"/>.
	/// </remarks>
	public string? SourcePath { get; private set; }

	IFileSystem? _sourceFileSystem;

	/// <summary>
	/// Gets the size of the metadata section in bytes (magic + all metadata blocks).
	/// </summary>
	public int MetadataSize { get; private set; }

	/// <summary>
	/// Gets or sets the CUESHEET block containing CD table of contents data.
	/// </summary>
	/// <remarks>
	/// May be null if the file has no CUESHEET block.
	/// </remarks>
	public FlacCueSheet? CueSheet { get; set; }

	/// <summary>
	/// Gets the raw STREAMINFO block data.
	/// </summary>
	/// <remarks>
	/// STREAMINFO is preserved as raw bytes since it's not typically modified.
	/// </remarks>
	public BinaryData StreamInfoData { get; }

	/// <summary>
	/// Gets the minimum block size in samples used in the file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FLAC audio is divided into blocks of samples. This is the minimum number of samples
	/// per block used anywhere in the file. The value is stored in STREAMINFO bytes 0-1.
	/// </para>
	/// <para>
	/// For fixed-blocksize streams, this equals <see cref="MaxBlockSize"/>.
	/// Valid range: 16 to 65535.
	/// </para>
	/// </remarks>
	public int MinBlockSize => StreamInfoData.Length >= 2
		? (StreamInfoData.Span[0] << 8) | StreamInfoData.Span[1]
		: 0;

	/// <summary>
	/// Gets the maximum block size in samples used in the file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// FLAC audio is divided into blocks of samples. This is the maximum number of samples
	/// per block used anywhere in the file. The value is stored in STREAMINFO bytes 2-3.
	/// </para>
	/// <para>
	/// For fixed-blocksize streams, this equals <see cref="MinBlockSize"/>.
	/// Valid range: 16 to 65535.
	/// </para>
	/// </remarks>
	public int MaxBlockSize => StreamInfoData.Length >= 4
		? (StreamInfoData.Span[2] << 8) | StreamInfoData.Span[3]
		: 0;

	/// <summary>
	/// Gets the minimum frame size in bytes, or 0 if unknown.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A frame is the basic unit of encoding, containing a frame header, subframes for each
	/// channel, and zero-padding to byte alignment. This is the minimum frame size in the file.
	/// The value is stored in STREAMINFO bytes 4-6 as a 24-bit big-endian integer.
	/// </para>
	/// <para>
	/// A value of 0 means the minimum frame size is unknown (common for streaming encodes).
	/// </para>
	/// </remarks>
	public int MinFrameSize => StreamInfoData.Length >= 7
		? (StreamInfoData.Span[4] << 16) | (StreamInfoData.Span[5] << 8) | StreamInfoData.Span[6]
		: 0;

	/// <summary>
	/// Gets the maximum frame size in bytes, or 0 if unknown.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A frame is the basic unit of encoding, containing a frame header, subframes for each
	/// channel, and zero-padding to byte alignment. This is the maximum frame size in the file.
	/// The value is stored in STREAMINFO bytes 7-9 as a 24-bit big-endian integer.
	/// </para>
	/// <para>
	/// A value of 0 means the maximum frame size is unknown (common for streaming encodes).
	/// </para>
	/// </remarks>
	public int MaxFrameSize => StreamInfoData.Length >= 10
		? (StreamInfoData.Span[7] << 16) | (StreamInfoData.Span[8] << 8) | StreamInfoData.Span[9]
		: 0;

	/// <summary>
	/// Gets the MD5 signature of the unencoded audio data from the STREAMINFO block.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The MD5 signature is a 128-bit (16-byte) hash of the original unencoded audio data.
	/// This can be used to verify that decoded audio matches the original recording.
	/// </para>
	/// <para>
	/// A value of all zeros indicates the MD5 was not computed when the file was encoded
	/// (some encoders skip this for performance).
	/// </para>
	/// </remarks>
	public ReadOnlySpan<byte> AudioMd5Signature => StreamInfoData.Span.Length >= 34
		? StreamInfoData.Span.Slice (18, 16)
		: ReadOnlySpan<byte>.Empty;

	/// <summary>
	/// Gets the MD5 signature as a lowercase hexadecimal string.
	/// </summary>
	/// <returns>
	/// A 32-character lowercase hex string, or null if STREAMINFO is invalid.
	/// Returns "00000000000000000000000000000000" if MD5 was not computed during encoding.
	/// </returns>
	public string? AudioMd5SignatureHex => StreamInfoData.Length >= 34
		? StreamInfoData.Slice (18, 16).ToHexString ()
		: null;

	/// <summary>
	/// Gets a value indicating whether the audio MD5 signature was computed.
	/// </summary>
	/// <remarks>
	/// Returns false if the MD5 signature is all zeros, which indicates the encoder
	/// did not compute it (e.g., for faster encoding).
	/// </remarks>
	public bool HasAudioMd5Signature {
		get {
			if (StreamInfoData.Span.Length < 34)
				return false;
			var md5 = StreamInfoData.Span.Slice (18, 16);
			for (var i = 0; i < 16; i++) {
				if (md5[i] != 0)
					return true;
			}
			return false;
		}
	}

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
	/// Gets the list of preserved metadata blocks (SEEKTABLE, APPLICATION, etc.).
	/// </summary>
	/// <remarks>
	/// These blocks are preserved during read and written back during save
	/// to maintain compatibility with applications that depend on them.
	/// </remarks>
	public IReadOnlyList<FlacPreservedBlock> PreservedBlocks => _preservedBlocks;

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

	/// <inheritdoc />
	public Tag? Tag => VorbisComment;

	/// <inheritdoc />
	IMediaProperties? IMediaFile.AudioProperties => Properties;

	/// <inheritdoc />
	VideoProperties? IMediaFile.VideoProperties => null;

	/// <inheritdoc />
	ImageProperties? IMediaFile.ImageProperties => null;

	/// <inheritdoc />
	MediaTypes IMediaFile.MediaTypes => Properties.IsValid ? MediaTypes.Audio : MediaTypes.None;

	/// <inheritdoc />
	public MediaFormat Format => MediaFormat.Flac;

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

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
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

		var result = Read (readResult.Data!.Value.Span);
		if (result.IsSuccess) {
			result.File!.SourcePath = path;
			result.File._sourceFileSystem = fileSystem;
		}
		return result;
	}

	/// <summary>
	/// Attempts to read a FLAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <returns>A result indicating success or failure.</returns>
	public static FlacFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (data.Length < MagicSize)
			return FlacFileReadResult.Failure ("Invalid FLAC file: data too short for header");

		// Verify magic
		if (data[0] != FlacMagic[0] || data[1] != FlacMagic[1] ||
			data[2] != FlacMagic[2] || data[3] != FlacMagic[3])
			return FlacFileReadResult.Failure ("Invalid FLAC file: missing magic (expected 'fLaC')");

		var offset = MagicSize;

		// Read STREAMINFO (must be first)
		if (offset + FlacMetadataBlockHeader.HeaderSize > data.Length)
			return FlacFileReadResult.Failure ("Invalid FLAC file: data too short for STREAMINFO header");

		var headerResult = FlacMetadataBlockHeader.Read (data.Slice (offset, FlacMetadataBlockHeader.HeaderSize));
		if (!headerResult.IsSuccess)
			return FlacFileReadResult.Failure (headerResult.Error!);

		if (headerResult.Header.BlockType != FlacBlockType.StreamInfo)
			return FlacFileReadResult.Failure ("Invalid FLAC file: first block must be STREAMINFO");

		// Per RFC 9639, STREAMINFO is always exactly 34 bytes
		const int StreamInfoSize = 34;
		if (headerResult.Header.DataLength != StreamInfoSize)
			return FlacFileReadResult.Failure ($"Invalid FLAC file: invalid STREAMINFO size (expected {StreamInfoSize}, got {headerResult.Header.DataLength})");

		offset += FlacMetadataBlockHeader.HeaderSize;

		if (offset + headerResult.Header.DataLength > data.Length)
			return FlacFileReadResult.Failure ("Invalid FLAC file: STREAMINFO data extends beyond file");

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

				case FlacBlockType.CueSheet:
					var cueSheetResult = FlacCueSheet.Read (blockData);
					if (cueSheetResult.IsSuccess)
						file.CueSheet = cueSheetResult.CueSheet;
					break;

				case FlacBlockType.SeekTable:
				case FlacBlockType.Application:
					// Preserve these blocks to write back during save
					file._preservedBlocks.Add (new FlacPreservedBlock (
						headerResult.Header.BlockType,
						new BinaryData (blockData)));
					break;

					// PADDING is not preserved - we generate fresh padding on save
			}
		}

		file.MetadataSize = offset;
		return FlacFileReadResult.Success (file, offset);
	}

	/// <summary>
	/// Attempts to read a FLAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (ReadOnlySpan<byte> data, out FlacFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <summary>
	/// Attempts to read a FLAC file from binary data.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="file">When successful, contains the parsed file.</param>
	/// <returns>True if parsing succeeded; otherwise, false.</returns>
	public static bool TryRead (BinaryData data, out FlacFile? file) =>
		TryRead (data.Span, out file);

	/// <summary>
	/// Checks if the data appears to be a valid FLAC file without fully parsing it.
	/// </summary>
	/// <param name="data">The data to check.</param>
	/// <returns>True if the data starts with the FLAC magic bytes "fLaC".</returns>
	public static bool IsValidFormat (ReadOnlySpan<byte> data) =>
		data.Length >= MagicSize &&
		data[0] == FlacMagic[0] && data[1] == FlacMagic[1] &&
		data[2] == FlacMagic[2] && data[3] == FlacMagic[3];

	VorbisComment EnsureVorbisComment ()
	{
		VorbisComment ??= new VorbisComment ("TagLibSharp2");
		return VorbisComment;
	}

	/// <summary>
	/// Saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data containing audio frames.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem = null)
	{
		var rendered = Render (originalData);
		if (rendered.IsEmpty)
			return FileWriteResult.Failure ("Failed to render FLAC file");

		return AtomicFileWriter.Write (path, rendered.Span, fileSystem);
	}

	/// <summary>
	/// Asynchronously saves the file to the specified path using atomic write.
	/// </summary>
	/// <param name="path">The target file path.</param>
	/// <param name="originalData">The original file data containing audio frames.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		string path,
		ReadOnlyMemory<byte> originalData,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		var rendered = Render (originalData.Span);
		if (rendered.IsEmpty)
			return Task.FromResult (FileWriteResult.Failure ("Failed to render FLAC file"));

		return AtomicFileWriter.WriteAsync (path, rendered.Memory, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path asynchronously, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// This convenience method re-reads the original file data from <see cref="SourcePath"/>
	/// and saves to the specified path. Requires that the file was read using
	/// <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public async Task<FileWriteResult> SaveToFileAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = await FileHelper.SafeReadAllBytesAsync (SourcePath!, fs, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return await SaveToFileAsync (path, readResult.Data!.Value, fileSystem, cancellationToken)
			.ConfigureAwait (false);
	}

	/// <summary>
	/// Saves the file back to its source path asynchronously.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task containing a result indicating success or failure.</returns>
	public Task<FileWriteResult> SaveToFileAsync (
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return Task.FromResult (FileWriteResult.Failure ("No source path available. File was not read from disk."));

		return SaveToFileAsync (SourcePath!, fileSystem, cancellationToken);
	}

	/// <summary>
	/// Saves the file to the specified path, re-reading from the source file.
	/// </summary>
	/// <remarks>
	/// This convenience method re-reads the original file data from <see cref="SourcePath"/>
	/// and saves to the specified path. Requires that the file was read using
	/// <see cref="ReadFromFile"/> or <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="path">The target file path.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (string path, IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		var fs = fileSystem ?? _sourceFileSystem;
		var readResult = FileHelper.SafeReadAllBytes (SourcePath!, fs);
		if (!readResult.IsSuccess)
			return FileWriteResult.Failure ($"Failed to re-read source file: {readResult.Error}");

		return SaveToFile (path, readResult.Data!.Value.Span, fileSystem);
	}

	/// <summary>
	/// Saves the file back to its source path.
	/// </summary>
	/// <remarks>
	/// This convenience method saves the file back to the path it was read from.
	/// Requires that the file was read using <see cref="ReadFromFile"/> or
	/// <see cref="ReadFromFileAsync"/>.
	/// </remarks>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result indicating success or failure.</returns>
	public FileWriteResult SaveToFile (IFileSystem? fileSystem = null)
	{
		if (string.IsNullOrEmpty (SourcePath))
			return FileWriteResult.Failure ("No source path available. File was not read from disk.");

		return SaveToFile (SourcePath!, fileSystem);
	}

	/// <summary>
	/// Renders the complete FLAC file including metadata and audio frames.
	/// </summary>
	/// <param name="originalData">The original file data containing audio frames.</param>
	/// <returns>The complete file data, or empty if rendering failed.</returns>
	public BinaryData Render (ReadOnlySpan<byte> originalData)
	{
		// Validate that we have audio data to preserve
		if (originalData.Length < MetadataSize)
			return BinaryData.Empty;

		var audioData = originalData.Slice (MetadataSize);

		// Render metadata blocks
		var vorbisCommentData = VorbisComment?.Render () ?? BinaryData.Empty;
		var cueSheetData = CueSheet?.Render () ?? BinaryData.Empty;
		var pictureDataList = new List<BinaryData> (_pictures.Count);
		for (var i = 0; i < _pictures.Count; i++)
			pictureDataList.Add (_pictures[i].RenderContent ());

		// Calculate total metadata size
		var totalMetadataSize = MagicSize
			+ FlacMetadataBlockHeader.HeaderSize + StreamInfoData.Length; // STREAMINFO

		if (!vorbisCommentData.IsEmpty)
			totalMetadataSize += FlacMetadataBlockHeader.HeaderSize + vorbisCommentData.Length;

		if (!cueSheetData.IsEmpty)
			totalMetadataSize += FlacMetadataBlockHeader.HeaderSize + cueSheetData.Length;

		for (var i = 0; i < pictureDataList.Count; i++)
			totalMetadataSize += FlacMetadataBlockHeader.HeaderSize + pictureDataList[i].Length;

		// Add preserved blocks (SEEKTABLE, APPLICATION)
		for (var i = 0; i < _preservedBlocks.Count; i++)
			totalMetadataSize += FlacMetadataBlockHeader.HeaderSize + _preservedBlocks[i].Data.Length;

		// Add padding block (4K is common)
		const int PaddingSize = 4096;
		totalMetadataSize += FlacMetadataBlockHeader.HeaderSize + PaddingSize;

		var totalSize = totalMetadataSize + audioData.Length;
		using var builder = new BinaryDataBuilder (totalSize);

		// Magic
		builder.Add (FlacMagic);

		// STREAMINFO (never last - we always have padding at minimum)
		var streamInfoHeader = new FlacMetadataBlockHeader (
			isLast: false,
			FlacBlockType.StreamInfo,
			StreamInfoData.Length);
		builder.Add (streamInfoHeader.Render ());
		builder.Add (StreamInfoData);

		// VORBIS_COMMENT
		if (!vorbisCommentData.IsEmpty) {
			var commentHeader = new FlacMetadataBlockHeader (isLast: false, FlacBlockType.VorbisComment, vorbisCommentData.Length);
			builder.Add (commentHeader.Render ());
			builder.Add (vorbisCommentData);
		}

		// CUESHEET
		if (!cueSheetData.IsEmpty) {
			var cueSheetHeader = new FlacMetadataBlockHeader (isLast: false, FlacBlockType.CueSheet, cueSheetData.Length);
			builder.Add (cueSheetHeader.Render ());
			builder.Add (cueSheetData);
		}

		// PICTURE blocks
		for (var i = 0; i < pictureDataList.Count; i++) {
			var pictureHeader = new FlacMetadataBlockHeader (isLast: false, FlacBlockType.Picture, pictureDataList[i].Length);
			builder.Add (pictureHeader.Render ());
			builder.Add (pictureDataList[i]);
		}

		// Preserved blocks (SEEKTABLE, APPLICATION)
		for (var i = 0; i < _preservedBlocks.Count; i++) {
			var block = _preservedBlocks[i];
			var blockHeader = new FlacMetadataBlockHeader (isLast: false, block.BlockType, block.Data.Length);
			builder.Add (blockHeader.Render ());
			builder.Add (block.Data);
		}

		// PADDING block (always last)
		var paddingHeader = new FlacMetadataBlockHeader (isLast: true, FlacBlockType.Padding, PaddingSize);
		builder.Add (paddingHeader.Render ());
		builder.AddZeros (PaddingSize);

		// Audio frames
		builder.Add (audioData);

		return builder.ToBinaryData ();
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

	/// <summary>
	/// Releases resources used by this instance.
	/// </summary>
	/// <remarks>
	/// Clears internal collections to allow garbage collection.
	/// </remarks>
	public void Dispose ()
	{
		if (_disposed)
			return;

		_pictures.Clear ();
		_preservedBlocks.Clear ();
		VorbisComment = null;
		CueSheet = null;
		SourcePath = null;
		_sourceFileSystem = null;
		_disposed = true;
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

/// <summary>
/// Represents a FLAC metadata block that is preserved during read/write operations.
/// </summary>
/// <remarks>
/// Used for blocks like SEEKTABLE and APPLICATION that are not directly modified
/// by the library but must be preserved to maintain file compatibility.
/// </remarks>
public readonly struct FlacPreservedBlock : IEquatable<FlacPreservedBlock>
{
	/// <summary>
	/// Gets the type of the metadata block.
	/// </summary>
	public FlacBlockType BlockType { get; }

	/// <summary>
	/// Gets the raw data of the metadata block (excluding the 4-byte header).
	/// </summary>
	public BinaryData Data { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="FlacPreservedBlock"/> struct.
	/// </summary>
	/// <param name="blockType">The type of metadata block.</param>
	/// <param name="data">The raw block data.</param>
	public FlacPreservedBlock (FlacBlockType blockType, BinaryData data)
	{
		BlockType = blockType;
		Data = data;
	}

	/// <inheritdoc/>
	public bool Equals (FlacPreservedBlock other) =>
		BlockType == other.BlockType && Data.Equals (other.Data);

	/// <inheritdoc/>
	public override bool Equals (object? obj) =>
		obj is FlacPreservedBlock other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (BlockType, Data);

	/// <summary>
	/// Determines whether two blocks are equal.
	/// </summary>
	public static bool operator == (FlacPreservedBlock left, FlacPreservedBlock right) =>
		left.Equals (right);

	/// <summary>
	/// Determines whether two blocks are not equal.
	/// </summary>
	public static bool operator != (FlacPreservedBlock left, FlacPreservedBlock right) =>
		!left.Equals (right);
}
