// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Core;

/// <summary>
/// Provides factory methods to open media files with automatic format detection.
/// </summary>
/// <remarks>
/// <para>
/// This class auto-detects the file format and returns the appropriate file object.
/// Supported formats include:
/// </para>
/// <list type="bullet">
/// <item>MP3 (ID3v1, ID3v2)</item>
/// <item>FLAC (Vorbis Comment, FLAC metadata)</item>
/// <item>Ogg Vorbis (Vorbis Comment)</item>
/// <item>Ogg Opus (Vorbis Comment)</item>
/// <item>WAV (RIFF INFO, ID3v2)</item>
/// <item>AIFF (ID3 chunk)</item>
/// <item>MP4/M4A (iTunes metadata)</item>
/// <item>DSF (DSD Stream File - ID3v2 metadata)</item>
/// <item>DFF/DSDIFF (DSD Interchange File Format - ID3v2 metadata)</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var result = MediaFile.Open("song.flac");
/// if (result.IsSuccess)
/// {
///     Console.WriteLine($"Title: {result.Tag?.Title}");
/// }
/// </code>
/// </example>
public static class MediaFile
{
	// Magic bytes for format detection
	static readonly byte[] FlacMagic = [(byte)'f', (byte)'L', (byte)'a', (byte)'C'];
	static readonly byte[] OggMagic = [(byte)'O', (byte)'g', (byte)'g', (byte)'S'];
	static readonly byte[] Id3Magic = [(byte)'I', (byte)'D', (byte)'3'];
	static readonly byte[] RiffMagic = [(byte)'R', (byte)'I', (byte)'F', (byte)'F'];
	static readonly byte[] WaveId = [(byte)'W', (byte)'A', (byte)'V', (byte)'E'];
	static readonly byte[] FormMagic = [(byte)'F', (byte)'O', (byte)'R', (byte)'M'];
	static readonly byte[] AiffId = [(byte)'A', (byte)'I', (byte)'F', (byte)'F'];
	static readonly byte[] AifcId = [(byte)'A', (byte)'I', (byte)'F', (byte)'C'];
	static readonly byte[] OpusHeadMagic = [(byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'H', (byte)'e', (byte)'a', (byte)'d'];
	static readonly byte[] VorbisMagic = [(byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];
	static readonly byte[] FtypMagic = [(byte)'f', (byte)'t', (byte)'y', (byte)'p'];
	static readonly byte[] DsfMagic = [(byte)'D', (byte)'S', (byte)'D', (byte)' '];
	static readonly byte[] Frm8Magic = [(byte)'F', (byte)'R', (byte)'M', (byte)'8'];

	/// <summary>
	/// Opens a media file and returns a unified result.
	/// </summary>
	/// <param name="path">The path to the media file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result containing the opened file or an error.</returns>
	public static MediaFileResult Open (string path, IFileSystem? fileSystem = null)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif

		var readResult = FileHelper.SafeReadAllBytes (path, fileSystem);
		if (!readResult.IsSuccess)
			return MediaFileResult.Failure (readResult.Error!);

		return OpenFromData (readResult.Data!, path);
	}

	/// <summary>
	/// Opens a media file asynchronously and returns a unified result.
	/// </summary>
	/// <param name="path">The path to the media file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result containing the opened file or an error.</returns>
	public static async Task<MediaFileResult> OpenAsync (
		string path,
		IFileSystem? fileSystem = null,
		CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif

		var readResult = await FileHelper.SafeReadAllBytesAsync (path, fileSystem, cancellationToken)
			.ConfigureAwait (false);
		if (!readResult.IsSuccess)
			return MediaFileResult.Failure (readResult.Error!);

		return OpenFromData (readResult.Data!, path);
	}

	/// <summary>
	/// Opens a media file from raw bytes.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="pathHint">Optional file path hint for extension-based detection.</param>
	/// <returns>A result containing the opened file or an error.</returns>
	public static MediaFileResult OpenFromData (ReadOnlyMemory<byte> data, string? pathHint = null)
	{
		var format = DetectFormat (data.Span, pathHint);

		return format switch {
			MediaFormat.Flac => OpenFlac (data),
			MediaFormat.OggVorbis => OpenOggVorbis (data),
			MediaFormat.Opus => OpenOpus (data),
			MediaFormat.Mp3 => OpenMp3 (data),
			MediaFormat.Wav => OpenWav (data),
			MediaFormat.Aiff => OpenAiff (data),
			MediaFormat.Mp4 => OpenMp4 (data),
			MediaFormat.Dsf => OpenDsf (data),
			MediaFormat.Dff => OpenDff (data),
			_ => MediaFileResult.Failure ($"Unknown or unsupported file format{(pathHint is not null ? $": {pathHint}" : "")}")
		};
	}

	/// <summary>
	/// Detects the format of a media file.
	/// </summary>
	/// <param name="data">The file data (at least first 12 bytes).</param>
	/// <param name="pathHint">Optional file path for extension fallback.</param>
	/// <returns>The detected format.</returns>
	public static MediaFormat DetectFormat (ReadOnlySpan<byte> data, string? pathHint = null)
	{
		// Check magic bytes first
		if (data.Length >= 4) {
			// FLAC: starts with "fLaC"
			if (data[0] == FlacMagic[0] && data[1] == FlacMagic[1] &&
				data[2] == FlacMagic[2] && data[3] == FlacMagic[3])
				return MediaFormat.Flac;

			// Ogg: starts with "OggS" - need to check first packet to distinguish Opus from Vorbis
			if (data[0] == OggMagic[0] && data[1] == OggMagic[1] &&
				data[2] == OggMagic[2] && data[3] == OggMagic[3])
				return DetectOggCodec (data);

			// RIFF: starts with "RIFF" + 4 bytes size + "WAVE"
			if (data.Length >= 12 &&
				data[0] == RiffMagic[0] && data[1] == RiffMagic[1] &&
				data[2] == RiffMagic[2] && data[3] == RiffMagic[3] &&
				data[8] == WaveId[0] && data[9] == WaveId[1] &&
				data[10] == WaveId[2] && data[11] == WaveId[3])
				return MediaFormat.Wav;

			// AIFF/AIFC: starts with "FORM" + 4 bytes size + "AIFF" or "AIFC"
			if (data.Length >= 12 &&
				data[0] == FormMagic[0] && data[1] == FormMagic[1] &&
				data[2] == FormMagic[2] && data[3] == FormMagic[3] &&
				((data[8] == AiffId[0] && data[9] == AiffId[1] &&
				  data[10] == AiffId[2] && data[11] == AiffId[3]) ||
				 (data[8] == AifcId[0] && data[9] == AifcId[1] &&
				  data[10] == AifcId[2] && data[11] == AifcId[3])))
				return MediaFormat.Aiff;

			// MP4/M4A: box structure with "ftyp" at offset 4
			if (data.Length >= 8 &&
				data[4] == FtypMagic[0] && data[5] == FtypMagic[1] &&
				data[6] == FtypMagic[2] && data[7] == FtypMagic[3])
				return MediaFormat.Mp4;

			// DSF: starts with "DSD "
			if (data[0] == DsfMagic[0] && data[1] == DsfMagic[1] &&
				data[2] == DsfMagic[2] && data[3] == DsfMagic[3])
				return MediaFormat.Dsf;

			// DFF (DSDIFF): starts with "FRM8" + 8 bytes size + "DSD "
			if (data.Length >= 16 &&
				data[0] == Frm8Magic[0] && data[1] == Frm8Magic[1] &&
				data[2] == Frm8Magic[2] && data[3] == Frm8Magic[3] &&
				data[12] == DsfMagic[0] && data[13] == DsfMagic[1] &&
				data[14] == DsfMagic[2] && data[15] == DsfMagic[3])
				return MediaFormat.Dff;
		}

		if (data.Length >= 3) {
			// ID3v2: starts with "ID3"
			if (data[0] == Id3Magic[0] && data[1] == Id3Magic[1] && data[2] == Id3Magic[2])
				return MediaFormat.Mp3;
		}

		// Check for MP3 frame sync (0xFF 0xFB, 0xFF 0xFA, 0xFF 0xF3, 0xFF 0xF2)
		if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
			return MediaFormat.Mp3;

		// Fall back to extension
		if (!string.IsNullOrEmpty (pathHint)) {
			var ext = Path.GetExtension (pathHint).ToUpperInvariant ();
			return ext switch {
				".FLAC" => MediaFormat.Flac,
				".OGG" => MediaFormat.OggVorbis,
				".OPUS" => MediaFormat.Opus,
				".MP3" => MediaFormat.Mp3,
				".WAV" => MediaFormat.Wav,
				".AIF" or ".AIFF" or ".AIFC" => MediaFormat.Aiff,
				".M4A" or ".M4B" or ".M4P" or ".M4V" or ".MP4" => MediaFormat.Mp4,
				".DSF" => MediaFormat.Dsf,
				".DFF" => MediaFormat.Dff,
				_ => MediaFormat.Unknown
			};
		}

		return MediaFormat.Unknown;
	}

	/// <summary>
	/// Detects the specific codec inside an Ogg container by examining the first packet.
	/// </summary>
	static MediaFormat DetectOggCodec (ReadOnlySpan<byte> data)
	{
		// Ogg page structure:
		// 0-3: "OggS" magic
		// 4: version (0)
		// 5: flags
		// 6-13: granule position
		// 14-17: serial number
		// 18-21: page sequence number
		// 22-25: CRC
		// 26: segment count
		// 27+: segment table (segment_count bytes)
		// After segment table: page data

		if (data.Length < 28)
			return MediaFormat.OggVorbis; // Default fallback

		var segmentCount = data[26];
		if (data.Length < 27 + segmentCount + 8) // Need at least 8 bytes of data for magic
			return MediaFormat.OggVorbis;

		// Calculate where page data starts
		var dataStart = 27 + segmentCount;

		// Check for OpusHead magic in first packet
		var packetData = data.Slice (dataStart);
		if (packetData.Length >= 8 &&
			packetData[0] == OpusHeadMagic[0] && packetData[1] == OpusHeadMagic[1] &&
			packetData[2] == OpusHeadMagic[2] && packetData[3] == OpusHeadMagic[3] &&
			packetData[4] == OpusHeadMagic[4] && packetData[5] == OpusHeadMagic[5] &&
			packetData[6] == OpusHeadMagic[6] && packetData[7] == OpusHeadMagic[7])
			return MediaFormat.Opus;

		// Check for Vorbis identification header (type 1 + "vorbis")
		if (packetData.Length >= 7 &&
			packetData[0] == 1 && // Type 1 = identification header
			packetData[1] == VorbisMagic[0] && packetData[2] == VorbisMagic[1] &&
			packetData[3] == VorbisMagic[2] && packetData[4] == VorbisMagic[3] &&
			packetData[5] == VorbisMagic[4] && packetData[6] == VorbisMagic[5])
			return MediaFormat.OggVorbis;

		// Unknown Ogg codec - default to Vorbis for backwards compatibility
		return MediaFormat.OggVorbis;
	}

	static MediaFileResult OpenFlac (ReadOnlyMemory<byte> data)
	{
		var result = FlacFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.VorbisComment, MediaFormat.Flac);
	}

	static MediaFileResult OpenOggVorbis (ReadOnlyMemory<byte> data)
	{
		var result = OggVorbisFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.VorbisComment, MediaFormat.OggVorbis);
	}

	static MediaFileResult OpenOpus (ReadOnlyMemory<byte> data)
	{
		var result = OggOpusFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.VorbisComment, MediaFormat.Opus);
	}

	static MediaFileResult OpenMp3 (ReadOnlyMemory<byte> data)
	{
		var result = Mp3File.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		// Prefer ID3v2 tag, fall back to ID3v1
		Tag? tag = result.File!.Id3v2Tag is not null
			? result.File.Id3v2Tag
			: result.File.Id3v1Tag;
		return MediaFileResult.Success (result.File, tag, MediaFormat.Mp3);
	}

	static MediaFileResult OpenWav (ReadOnlyMemory<byte> data)
	{
		var binaryData = new BinaryData (data.Span);
		var file = WavFile.ReadFromData (binaryData);
		if (file is null)
			return MediaFileResult.Failure ("Failed to parse WAV file");

		// Prefer ID3v2 tag, fall back to RIFF INFO tag
		Tag? tag = file.Id3v2Tag is not null
			? file.Id3v2Tag
			: file.InfoTag;
		return MediaFileResult.Success (file, tag, MediaFormat.Wav);
	}

	static MediaFileResult OpenAiff (ReadOnlyMemory<byte> data)
	{
		var binaryData = new BinaryData (data.Span);
		if (!AiffFile.TryParse (binaryData, out var file) || file is null)
			return MediaFileResult.Failure ("Failed to parse AIFF file");

		return MediaFileResult.Success (file, file.Tag, MediaFormat.Aiff);
	}

	static MediaFileResult OpenMp4 (ReadOnlyMemory<byte> data)
	{
		var result = Mp4File.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Tag, MediaFormat.Mp4);
	}

	static MediaFileResult OpenDsf (ReadOnlyMemory<byte> data)
	{
		var result = DsfFile.Parse (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Id3v2Tag, MediaFormat.Dsf);
	}

	static MediaFileResult OpenDff (ReadOnlyMemory<byte> data)
	{
		var result = DffFile.Parse (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Id3v2Tag, MediaFormat.Dff);
	}
}

/// <summary>
/// Represents the result of opening a media file.
/// </summary>
public sealed class MediaFileResult
{
	/// <summary>
	/// Gets a value indicating whether the file was successfully opened.
	/// </summary>
	public bool IsSuccess { get; }

	/// <summary>
	/// Gets the error message if the operation failed.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the underlying file object (FlacFile, Mp3File, etc.).
	/// </summary>
	public object? File { get; }

	/// <summary>
	/// Gets the tag from the file (VorbisComment, Id3v2Tag, etc.).
	/// </summary>
	public Tag? Tag { get; }

	/// <summary>
	/// Gets the detected media format.
	/// </summary>
	public MediaFormat Format { get; }

	MediaFileResult (bool isSuccess, string? error, object? file, Tag? tag, MediaFormat format)
	{
		IsSuccess = isSuccess;
		Error = error;
		File = file;
		Tag = tag;
		Format = format;
	}

	/// <summary>
	/// Creates a successful result.
	/// </summary>
	internal static MediaFileResult Success (object file, Tag? tag, MediaFormat format) =>
		new (true, null, file, tag, format);

	/// <summary>
	/// Creates a failed result.
	/// </summary>
	internal static MediaFileResult Failure (string error) =>
		new (false, error, null, null, MediaFormat.Unknown);

	/// <summary>
	/// Gets the file as a specific type.
	/// </summary>
	/// <typeparam name="T">The file type (FlacFile, Mp3File, etc.).</typeparam>
	/// <returns>The file if it matches the type, otherwise null.</returns>
	public T? GetFileAs<T> () where T : class => File as T;
}

/// <summary>
/// Represents supported media file formats.
/// </summary>
public enum MediaFormat
{
	/// <summary>
	/// Unknown or unsupported format.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// FLAC audio format.
	/// </summary>
	Flac,

	/// <summary>
	/// Ogg Vorbis audio format.
	/// </summary>
	OggVorbis,

	/// <summary>
	/// Ogg Opus audio format.
	/// </summary>
	Opus,

	/// <summary>
	/// MP3 audio format.
	/// </summary>
	Mp3,

	/// <summary>
	/// WAV audio format (RIFF container).
	/// </summary>
	Wav,

	/// <summary>
	/// AIFF/AIFC audio format (IFF container).
	/// </summary>
	Aiff,

	/// <summary>
	/// MP4/M4A audio format (MPEG-4 container).
	/// </summary>
	Mp4,

	/// <summary>
	/// DSF audio format (DSD Stream File - Sony's DSD container).
	/// </summary>
	Dsf,

	/// <summary>
	/// DFF audio format (DSDIFF - Philips' DSD Interchange File Format).
	/// </summary>
	Dff
}
