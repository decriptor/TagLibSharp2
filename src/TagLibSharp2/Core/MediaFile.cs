// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TagLibSharp2.Aac;
using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Audible;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Image;
using TagLibSharp2.Matroska;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.WavPack;
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
/// <item>WMA/ASF (Windows Media Audio - ASF extended content)</item>
/// <item>Matroska/WebM (Matroska tags)</item>
/// <item>MPEG program streams and MPEG video elementary streams (ID3v2, ID3v1)</item>
/// <item>AAC (raw ADTS or ADIF - ID3v2, APEv2, ID3v1)</item>
/// <item>Audible .aa (text dictionary). Audible .aax is MPEG-4 and opens as MP4.</item>
/// <item>AVI (RIFF INFO, ID3v2; OpenDML)</item>
/// </list>
///
/// <para><b>Reading and Modifying Tags</b></para>
/// <para>
/// Use <see cref="Read(string, IFileSystem)"/> or <see cref="ReadAsync(string, IFileSystem, CancellationToken)"/> to open a file. The result contains
/// the strongly-typed file object (e.g., <see cref="TagLibSharp2.Xiph.FlacFile"/>,
/// <see cref="TagLibSharp2.Mpeg.Mp3File"/>) with Tag and audio property access.
/// Modify tag properties directly on the file object.
/// </para>
///
/// <para><b>Saving Changes</b></para>
/// <para>
/// To save changes, call the file object's <c>Render(originalData)</c> method with the
/// original file bytes. This returns a new <see cref="BinaryData"/> representing the
/// modified file. Then use <see cref="AtomicFileWriter"/> for safe disk writes:
/// </para>
/// <list type="number">
/// <item>Creates a temporary file with changes</item>
/// <item>Verifies the temp file is valid</item>
/// <item>Atomically replaces the original (rename on Unix, replace on Windows)</item>
/// </list>
/// <para>
/// The <c>originalData</c> parameter is required because:
/// </para>
/// <list type="bullet">
/// <item>Audio data is NOT held in memory - only metadata is parsed</item>
/// <item>Render combines new metadata with original audio data</item>
/// <item>This keeps memory usage low for large files</item>
/// </list>
/// </remarks>
/// <example>
/// <para><b>Reading a file:</b></para>
/// <code>
/// var result = MediaFile.Read("song.flac");
/// if (result.IsSuccess)
/// {
///     Console.WriteLine($"Title: {result.Tag?.Title}");
/// }
/// </code>
///
/// <para><b>Complete read-modify-save workflow:</b></para>
/// <code>
/// // 1. Read the original file bytes (needed for rendering)
/// var originalBytes = File.ReadAllBytes("song.flac");
///
/// // 2. Parse the file
/// var result = MediaFile.ReadFromData(originalBytes, "song.flac");
/// if (!result.IsSuccess) return;
///
/// // 3. Modify tags through the strongly-typed file object
/// var flacFile = (FlacFile)result.File!;
/// flacFile.Tag.Title = "New Title";
/// flacFile.Tag.Artist = "New Artist";
///
/// // 4. Render the modified file (combines metadata + original audio)
/// var modifiedBytes = flacFile.Render(originalBytes);
///
/// // 5. Save atomically (safe disk write)
/// AtomicFileWriter.Write("song.flac", modifiedBytes);
/// </code>
///
/// <para><b>Using IMediaFile interface for polymorphic code:</b></para>
/// <code>
/// var result = MediaFile.Read("song.mp3");
/// if (result.IsSuccess)
/// {
///     IMediaFile file = result.File!;
///     file.Tag!.Title = "Works with any format";
///     // Note: Render/Save requires format-specific cast
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
	static readonly byte[] WavPackMagic = [(byte)'w', (byte)'v', (byte)'p', (byte)'k'];
	static readonly byte[] MonkeysAudioMagic = [(byte)'M', (byte)'A', (byte)'C', (byte)' '];
	static readonly byte[] OggFlacMagic = [0x7F, (byte)'F', (byte)'L', (byte)'A', (byte)'C'];
	static readonly byte[] AsfMagic = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
	static readonly byte[] MusepackSV7Magic = [(byte)'M', (byte)'P', (byte)'+'];
	static readonly byte[] MusepackSV8Magic = [(byte)'M', (byte)'P', (byte)'C', (byte)'K'];
	static readonly byte[] EbmlMagic = [0x1A, 0x45, 0xDF, 0xA3];

	/// <summary>
	/// Reads a media file and returns a unified result.
	/// </summary>
	/// <param name="path">The path to the media file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	public static MediaFileResult Read (string path, IFileSystem? fileSystem = null)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (path is null)
			throw new ArgumentNullException (nameof (path));
#else
		ArgumentNullException.ThrowIfNull (path);
#endif

		var fs = fileSystem ?? DefaultFileSystem.Instance;
		try {
			using var stream = fs.OpenRead (path);
			return Read (stream, path, path, fs);
		} catch (FileNotFoundException) {
			return MediaFileResult.Failure ($"File not found: {path}");
		} catch (DirectoryNotFoundException) {
			return MediaFileResult.Failure ($"File not found: {path}");
		} catch (IOException ex) {
			return MediaFileResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return MediaFileResult.Failure ($"Access denied: {ex.Message}");
		}
	}

	/// <summary>
	/// Reads a media file from a stream.
	/// </summary>
	/// <param name="stream">The stream to read. The caller keeps ownership of it.</param>
	/// <param name="pathHint">Optional file name used when the bytes do not identify a format.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	/// <remarks>
	/// A seekable stream is read by the ranges the format needs and does not read the
	/// audio or video payload. A stream that cannot seek is copied once.
	/// </remarks>
	public static MediaFileResult Read (Stream stream, string? pathHint = null)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (stream is null)
			throw new ArgumentNullException (nameof (stream));
#else
		ArgumentNullException.ThrowIfNull (stream);
#endif
		try {
			return Read (stream, pathHint, sourcePath: null, fileSystem: null);
		} catch (IOException ex) {
			return MediaFileResult.Failure (ex.Message);
		}
	}

	/// <summary>
	/// Reads a media file asynchronously and returns a unified result.
	/// </summary>
	/// <param name="path">The path to the media file.</param>
	/// <param name="fileSystem">Optional file system abstraction for testing.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	public static async Task<MediaFileResult> ReadAsync (
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

		var fs = fileSystem ?? DefaultFileSystem.Instance;
		try {
			using var stream = fs.OpenRead (path);
			return await ReadAsync (stream, path, path, fs, cancellationToken).ConfigureAwait (false);
		} catch (FileNotFoundException) {
			return MediaFileResult.Failure ($"File not found: {path}");
		} catch (DirectoryNotFoundException) {
			return MediaFileResult.Failure ($"File not found: {path}");
		} catch (IOException ex) {
			return MediaFileResult.Failure ($"Failed to read file: {ex.Message}");
		} catch (UnauthorizedAccessException ex) {
			return MediaFileResult.Failure ($"Access denied: {ex.Message}");
		} catch (OperationCanceledException) {
			return MediaFileResult.Failure ("Operation was cancelled");
		}
	}

	/// <summary>
	/// Reads a media file from a stream asynchronously.
	/// </summary>
	/// <param name="stream">The stream to read. The caller keeps ownership of it.</param>
	/// <param name="pathHint">Optional file name used when the bytes do not identify a format.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	/// <remarks>
	/// A seekable stream is read by the ranges the format needs and does not read the
	/// audio or video payload. A stream that cannot seek is copied once.
	/// </remarks>
	public static Task<MediaFileResult> ReadAsync (
		Stream stream,
		string? pathHint = null,
		CancellationToken cancellationToken = default)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		if (stream is null)
			throw new ArgumentNullException (nameof (stream));
#else
		ArgumentNullException.ThrowIfNull (stream);
#endif
		return ReadStreamAsync (stream, pathHint, cancellationToken);
	}

	static async Task<MediaFileResult> ReadStreamAsync (Stream stream, string? pathHint, CancellationToken cancellationToken)
	{
		try {
			return await ReadAsync (stream, pathHint, sourcePath: null, fileSystem: null, cancellationToken)
				.ConfigureAwait (false);
		} catch (IOException ex) {
			return MediaFileResult.Failure (ex.Message);
		}
	}

	static MediaFileResult Read (Stream stream, string? pathHint, string? sourcePath, IFileSystem? fileSystem)
	{
		if (!stream.CanSeek)
			return RememberMatroskaSource (ReadFromData (StreamBytes.ReadToEnd (stream), pathHint), sourcePath, fileSystem);

		if (stream.Position != 0)
			stream.Position = 0;

		var peek = ReadPeek (stream, pathHint);
		var format = DetectOpenFormat (stream, peek, pathHint);
		stream.Position = 0;
		return RememberMatroskaSource (
			OpenDetected (format, stream, sourcePath, fileSystem, pathHint),
			sourcePath,
			fileSystem);
	}

	// A read smaller than FileStream's 4096-byte buffer is cached, so the parser
	// reuses it. Ogg and unrecognized files still get a longer prefix.
	const int ShortPeekBytes = 32;
	const int LongPeekBytes = 4095;

	static byte[] ReadPeek (Stream stream, string? pathHint)
	{
		var peek = StreamBytes.ReadAtMost (stream, ShortPeekBytes);
		var want = PeekLength (peek, pathHint);
		if (want <= peek.Length)
			return peek;

		stream.Position = 0;
		return StreamBytes.ReadAtMost (stream, want);
	}

	static async Task<byte[]> ReadPeekAsync (Stream stream, string? pathHint, CancellationToken cancellationToken)
	{
		var peek = await StreamBytes.ReadAtMostAsync (stream, ShortPeekBytes, cancellationToken).ConfigureAwait (false);
		var want = PeekLength (peek, pathHint);
		if (want <= peek.Length)
			return peek;

		stream.Position = 0;
		return await StreamBytes.ReadAtMostAsync (stream, want, cancellationToken).ConfigureAwait (false);
	}

	// WAVE stays on the short peek. Legacy WavPack is decided by seeking chunk headers,
	// not by assuming the data chunk follows fmt. The extension is not consulted here: a file
	// whose magic the short peek misses gets the long peek, whatever its name says.
	static int PeekLength (ReadOnlySpan<byte> peek, string? pathHint)
	{
		if (IsOgg (peek) || (!IsRiffWave (peek) && DetectFormat (peek) == MediaFormat.Unknown))
			return LongPeekBytes;

		return peek.Length;
	}

	static MediaFormat DetectOpenFormat (Stream stream, ReadOnlySpan<byte> peek, string? pathHint)
	{
		if (IsRiffWave (peek))
			return RiffDataStartsWithWavPack (stream) ? MediaFormat.WavPack : MediaFormat.Wav;

		// The peek holds the ID3v2 header but not what follows the tag. A program stream behind the
		// tag is MPEG-PS, checked first: its PES packets may carry ADTS audio, which is not a raw AAC file.
		if (StartsWithId3 (peek) && MpegPsFile.StartsAfterId3v2 (stream))
			return MediaFormat.MpegPs;

		return DetectFormat (peek, pathHint, stream);
	}

	static bool StartsWithId3 (ReadOnlySpan<byte> data) =>
		data.Length >= 3 && data[0] == Id3Magic[0] && data[1] == Id3Magic[1] && data[2] == Id3Magic[2];

	// Walk RIFF chunk headers until "data". Payloads are skipped, not copied.
	// Each chunk before data may be at most MaxRiffChunkSkip. The data chunk
	// may start further in. "wvpk" at the start of its payload is legacy WavPack.
	static bool RiffDataStartsWithWavPack (Stream stream)
	{
		if (stream.Length < 12)
			return false;

		var cap = (long)WavPackFile.MaxRiffChunkSkip;
		stream.Position = 12;
		while (stream.Position + 8 <= stream.Length) {
			var header = StreamBytes.ReadAtMost (stream, 8);
			if (header.Length < 8)
				return false;

			var size = BinaryPrimitives.ReadUInt32LittleEndian (header.AsSpan (4, 4));
			if (IsChunkId (header, 0, (byte)'d', (byte)'a', (byte)'t', (byte)'a')) {
				var magic = StreamBytes.ReadAtMost (stream, WavPackMagic.Length);
				return magic.Length == WavPackMagic.Length && magic.AsSpan ().SequenceEqual (WavPackMagic);
			}

			var padded = (long)size + ((size & 1) == 1 ? 1L : 0L);
			if (padded > cap || padded > stream.Length - stream.Position)
				return false;

			stream.Position += padded;
		}

		return false;
	}

	static bool IsChunkId (ReadOnlySpan<byte> data, int offset, byte a, byte b, byte c, byte d) =>
		offset + 4 <= data.Length
		&& data[offset] == a
		&& data[offset + 1] == b
		&& data[offset + 2] == c
		&& data[offset + 3] == d;

	static bool IsRiffWave (ReadOnlySpan<byte> data) =>
		data.Length >= 12
		&& data[0] == RiffMagic[0] && data[1] == RiffMagic[1]
		&& data[2] == RiffMagic[2] && data[3] == RiffMagic[3]
		&& data[8] == WaveId[0] && data[9] == WaveId[1]
		&& data[10] == WaveId[2] && data[11] == WaveId[3];

	static bool IsOgg (ReadOnlySpan<byte> peek) =>
		peek.Length >= OggMagic.Length
		&& peek[0] == OggMagic[0]
		&& peek[1] == OggMagic[1]
		&& peek[2] == OggMagic[2]
		&& peek[3] == OggMagic[3];

	static async Task<MediaFileResult> ReadAsync (
		Stream stream,
		string? pathHint,
		string? sourcePath,
		IFileSystem? fileSystem,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested ();
		if (!stream.CanSeek)
			return RememberMatroskaSource (
				ReadFromData (await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false), pathHint),
				sourcePath,
				fileSystem);

		if (stream.Position != 0)
			stream.Position = 0;

		var peek = await ReadPeekAsync (stream, pathHint, cancellationToken).ConfigureAwait (false);
		var format = DetectOpenFormat (stream, peek, pathHint);
		stream.Position = 0;
		var result = format switch {
			MediaFormat.Mp3 => FromMp3 (await Mp3File.ReadAsync (stream, cancellationToken).ConfigureAwait (false), sourcePath, fileSystem),
			MediaFormat.Flac => FromFlac (await FlacFile.ReadAsync (stream, cancellationToken).ConfigureAwait (false), sourcePath, fileSystem),
			MediaFormat.Wav => FromWav (WavFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.MpegPs => FromMpegPs (await MpegPsFile.ReadAsync (stream, sourcePath, fileSystem, cancellationToken).ConfigureAwait (false)),
			MediaFormat.Audible => FromAudible (await AudibleFile.ReadAsync (stream, sourcePath, fileSystem, cancellationToken).ConfigureAwait (false)),
			MediaFormat.Avi => FromAvi (await AviFile.ReadAsync (stream, sourcePath, fileSystem, cancellationToken).ConfigureAwait (false)),
			MediaFormat.Jpeg or MediaFormat.Png or MediaFormat.Gif or MediaFormat.Bmp or MediaFormat.Pcx
				or MediaFormat.Netpbm or MediaFormat.Tiff or MediaFormat.Dng or MediaFormat.Svg
				=> OpenImage (await StreamBytes.ReadToEndAsync (stream, cancellationToken).ConfigureAwait (false), format),
			_ => OpenDetected (format, stream, sourcePath, fileSystem, pathHint)
		};
		return RememberMatroskaSource (result, sourcePath, fileSystem);
	}

	static MediaFileResult OpenDetected (MediaFormat format, Stream stream, string? sourcePath, IFileSystem? fileSystem, string? pathHint) =>
		format switch {
			MediaFormat.Mp3 => FromMp3 (Mp3File.Read (stream), sourcePath, fileSystem),
			MediaFormat.Flac => FromFlac (FlacFile.Read (stream), sourcePath, fileSystem),
			MediaFormat.Wav => FromWav (WavFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Mp4 => FromMp4 (Mp4File.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Matroska => FromMatroska (MatroskaFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.OggVorbis => FromOggVorbis (OggVorbisFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Opus => FromOpus (OggOpusFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.OggFlac => FromOggFlac (OggFlacFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Aiff => FromAiff (AiffFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.WavPack => FromWavPack (WavPackFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.MonkeysAudio => FromMonkeys (MonkeysAudioFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Musepack => FromMusepack (MusepackFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Dsf => FromDsf (DsfFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Dff => FromDff (DffFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Asf => FromAsf (AsfFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.MpegPs => FromMpegPs (MpegPsFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Aac => FromAac (AacFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Audible => FromAudible (AudibleFile.Read (stream, sourcePath, fileSystem)),
			MediaFormat.Avi => FromAvi (AviFile.Read (stream, sourcePath, fileSystem)),
			_ => ReadFromData (StreamBytes.ReadToEnd (stream), pathHint)
		};

	// ReadFromData's path is only an extension hint. A path read can save Matroska and still images back to that file.
	static MediaFileResult RememberMatroskaSource (MediaFileResult result, string? sourcePath, IFileSystem? fileSystem)
	{
		if (sourcePath is null || !result.IsSuccess)
			return result;
		if (result.File is MatroskaFile matroska)
			matroska.SetSourcePath (sourcePath, fileSystem);
		else if (result.File is StillImageFile image)
			image.SetSource (sourcePath, fileSystem);
		return result;
	}

	static MediaFileResult FromMp3 (Mp3FileReadResult result, string? sourcePath = null, IFileSystem? fileSystem = null)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse MP3 file");
		if (sourcePath is { Length: > 0 })
			result.File.RememberSource (sourcePath, fileSystem);
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Mp3);
	}

	static MediaFileResult FromFlac (FlacFileReadResult result, string? sourcePath = null, IFileSystem? fileSystem = null)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse FLAC file");
		if (sourcePath is { Length: > 0 })
			result.File.RememberSource (sourcePath, fileSystem);
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Flac);
	}

	static MediaFileResult FromMp4 (Mp4FileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse MP4 file");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Mp4);
	}

	static MediaFileResult FromMatroska (MatroskaFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Matroska file");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Matroska);
	}

	static MediaFileResult FromOggVorbis (OggVorbisFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Ogg Vorbis file");
		return MediaFileResult.Success (result.File, result.File.VorbisComment, MediaFormat.OggVorbis);
	}

	static MediaFileResult FromOpus (OggOpusFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Opus file");
		return MediaFileResult.Success (result.File, result.File.VorbisComment, MediaFormat.Opus);
	}

	static MediaFileResult FromOggFlac (OggFlacFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Ogg FLAC file");
		return MediaFileResult.Success (result.File, result.File.VorbisComment, MediaFormat.OggFlac);
	}

	static MediaFileResult FromAiff (AiffFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse AIFF file");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Aiff);
	}

	static MediaFileResult FromWavPack (WavPackFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse WavPack file");
		return MediaFileResult.Success (result.File, result.File.ApeTag, MediaFormat.WavPack);
	}

	static MediaFileResult FromMonkeys (MonkeysAudioFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Monkey's Audio file");
		return MediaFileResult.Success (result.File, result.File.ApeTag, MediaFormat.MonkeysAudio);
	}

	static MediaFileResult FromMusepack (MusepackFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Musepack file");
		return MediaFileResult.Success (result.File, result.File.ApeTag, MediaFormat.Musepack);
	}

	static MediaFileResult FromDsf (DsfFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse DSF file");
		return MediaFileResult.Success (result.File, result.File.Id3v2Tag, MediaFormat.Dsf);
	}

	static MediaFileResult FromDff (DffFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse DFF file");
		return MediaFileResult.Success (result.File, result.File.Id3v2Tag, MediaFormat.Dff);
	}

	static MediaFileResult FromAsf (AsfFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse ASF file");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Asf);
	}

	static MediaFileResult FromMpegPs (MpegPsFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse MPEG program stream");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.MpegPs);
	}

	static MediaFileResult FromAac (AacFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse AAC file");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Aac);
	}

	static MediaFileResult FromAudible (AudibleFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse Audible file");
		return MediaFileResult.Success (result.File, result.File.Tag, MediaFormat.Audible);
	}

	static MediaFileResult FromAvi (AviFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse AVI file");
		return MediaFileResult.Success (result.File, result.File.UnifiedTag, MediaFormat.Avi);
	}

	static MediaFileResult FromWav (WavFileReadResult result)
	{
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse WAV file");

		Tag? tag = result.File.Id3v2Tag is not null
			? result.File.Id3v2Tag
			: result.File.InfoTag;
		return MediaFileResult.Success (result.File, tag, MediaFormat.Wav);
	}

	/// <summary>
	/// Reads a media file from raw bytes.
	/// </summary>
	/// <param name="data">The file data.</param>
	/// <param name="pathHint">Optional file path hint for extension-based detection.</param>
	/// <returns>A result containing the parsed file or an error.</returns>
	public static MediaFileResult ReadFromData (ReadOnlyMemory<byte> data, string? pathHint = null) =>
		ReadCore (data, pathHint);

	static MediaFileResult ReadCore (ReadOnlyMemory<byte> data, string? pathHint)
	{
		var format = DetectFormat (data.Span, pathHint);

		var result = format switch {
			MediaFormat.Flac => OpenFlac (data),
			MediaFormat.OggVorbis => OpenOggVorbis (data),
			MediaFormat.Opus => OpenOpus (data),
			MediaFormat.OggFlac => OpenOggFlac (data),
			MediaFormat.Mp3 => OpenMp3 (data),
			MediaFormat.Wav => OpenWav (data),
			MediaFormat.Aiff => OpenAiff (data),
			MediaFormat.Mp4 => OpenMp4 (data),
			MediaFormat.Dsf => OpenDsf (data),
			MediaFormat.Dff => OpenDff (data),
			MediaFormat.WavPack => OpenWavPack (data),
			MediaFormat.MonkeysAudio => OpenMonkeysAudio (data),
			MediaFormat.Asf => OpenAsf (data),
			MediaFormat.Musepack => OpenMusepack (data),
			MediaFormat.Matroska => OpenMatroska (data),
			MediaFormat.MpegPs => FromMpegPs (MpegPsFile.Read (data.Span)),
			MediaFormat.Aac => FromAac (AacFile.Read (data)),
			MediaFormat.Audible => FromAudible (AudibleFile.Read (data.Span)),
			MediaFormat.Avi => FromAvi (AviFile.Read (data.Span)),
			MediaFormat.Jpeg or MediaFormat.Png or MediaFormat.Gif or MediaFormat.Bmp or MediaFormat.Pcx
				or MediaFormat.Netpbm or MediaFormat.Tiff or MediaFormat.Dng or MediaFormat.Svg => OpenImage (data, format),
			_ => MediaFileResult.Failure ($"Unknown or unsupported file format{(pathHint is not null ? $": {pathHint}" : "")}")
		};

		return result;
	}

	/// <summary>
	/// Detects the format of a media file.
	/// </summary>
	/// <param name="data">The file data (at least first 12 bytes).</param>
	/// <param name="pathHint">Optional file path for extension fallback.</param>
	/// <returns>The detected format.</returns>
	public static MediaFormat DetectFormat (ReadOnlySpan<byte> data, string? pathHint = null) =>
		DetectFormat (data, pathHint, stream: null);

	// A seekable stream answers the AAC question from the whole file. The peek alone holds
	// neither the bytes after a long ID3v2 tag nor the header after the first ADTS frame.
	static MediaFormat DetectFormat (ReadOnlySpan<byte> data, string? pathHint, Stream? stream)
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

			// RIFF/WAVE. Legacy WavPack keeps "wvpk" at the start of the data chunk.
			if (IsRiffWave (data))
				return WavPackFile.IsValidFormat (data) ? MediaFormat.WavPack : MediaFormat.Wav;

			// RIFF/AVI. The form type, not the extension, tells AVI from WAVE.
			if (AviFile.IsValidFormat (data))
				return MediaFormat.Avi;

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

			// WavPack: starts with "wvpk"
			if (data[0] == WavPackMagic[0] && data[1] == WavPackMagic[1] &&
				data[2] == WavPackMagic[2] && data[3] == WavPackMagic[3])
				return MediaFormat.WavPack;

			// Monkey's Audio: starts with "MAC "
			if (data[0] == MonkeysAudioMagic[0] && data[1] == MonkeysAudioMagic[1] &&
				data[2] == MonkeysAudioMagic[2] && data[3] == MonkeysAudioMagic[3])
				return MediaFormat.MonkeysAudio;

			// Matroska/WebM: EBML header 0x1A45DFA3
			if (data[0] == EbmlMagic[0] && data[1] == EbmlMagic[1] &&
				data[2] == EbmlMagic[2] && data[3] == EbmlMagic[3])
				return MediaFormat.Matroska;

			// Musepack SV8: starts with "MPCK"
			if (data[0] == MusepackSV8Magic[0] && data[1] == MusepackSV8Magic[1] &&
				data[2] == MusepackSV8Magic[2] && data[3] == MusepackSV8Magic[3])
				return MediaFormat.Musepack;

			// Audible .aa: file size, then the magic 0x57907536 at offset 4
			if (AudibleFile.IsValidFormat (data))
				return MediaFormat.Audible;

			// Musepack SV7: starts with "MP+"
			if (data[0] == MusepackSV7Magic[0] && data[1] == MusepackSV7Magic[1] &&
				data[2] == MusepackSV7Magic[2])
				return MediaFormat.Musepack;

			// MPEG program stream (pack header 0x000001BA) or video elementary stream
			// (sequence header 0x000001B3). Checked after MP4, whose first box size can
			// spell 0x000001BA in front of "ftyp".
			if (MpegPsFile.IsValidFormat (data))
				return MediaFormat.MpegPs;
		}

		// ASF/WMA: starts with ASF Header Object GUID (16 bytes)
		if (data.Length >= 16 && data.Slice (0, 16).SequenceEqual (AsfMagic))
			return MediaFormat.Asf;

		// ID3v2: starts with "ID3". The stream after the tag decides: a program stream is MPEG-PS; an ADTS
		// sync (layer '00') or "ADIF" is AAC (ISO/IEC 13818-7 §6.1, §6.2). Anything else stays MP3.
		if (data.Length >= 3 && data[0] == Id3Magic[0] && data[1] == Id3Magic[1] && data[2] == Id3Magic[2]) {
			if (MpegPsFile.IsValidFormat (data))
				return MediaFormat.MpegPs;
			return IsAac (data, stream) ? MediaFormat.Aac : MediaFormat.Mp3;
		}

		// Bare ADTS or ADIF. An MPEG audio sync has layer bits other than '00' and falls through.
		if (IsAac (data, stream))
			return MediaFormat.Aac;

		// A bare sync (0xFF plus the top 3 bits) also matches a UTF-16 LE BOM (FF FE).
		if (MpegFrame.IsStartOfAudio (data))
			return MediaFormat.Mp3;

		// After the audio signatures. A markup check that decoded every unknown buffer
		// would copy an MP3, an ID3 tag, or an ASF header on the way to "not an image".
		if (data.Length >= 4 && ImageHeader.TryDetect (data, out var image))
			return image == MediaFormat.Tiff && StillImageFile.HasDngName (pathHint) ? MediaFormat.Dng : image;

		// Fall back to extension
		if (!string.IsNullOrEmpty (pathHint)) {
			var ext = Path.GetExtension (pathHint).ToUpperInvariant ();
			return ext switch {
				".FLAC" => MediaFormat.Flac,
				".OGG" => MediaFormat.OggVorbis,
				".OGA" => MediaFormat.OggFlac, // Ogg FLAC typically uses .oga extension
				".OPUS" => MediaFormat.Opus,
				".MP3" => MediaFormat.Mp3,
				".WAV" => MediaFormat.Wav,
				".AIF" or ".AIFF" or ".AIFC" => MediaFormat.Aiff,
				".M4A" or ".M4B" or ".M4P" or ".M4V" or ".MP4" or ".AAX" => MediaFormat.Mp4,
				".AA" => MediaFormat.Audible,
				".DSF" => MediaFormat.Dsf,
				".DFF" => MediaFormat.Dff,
				".WV" => MediaFormat.WavPack,
				".APE" => MediaFormat.MonkeysAudio,
				".WMA" or ".WMV" or ".ASF" => MediaFormat.Asf,
				".MPC" or ".MP+" or ".MPP" => MediaFormat.Musepack,
				".MKV" or ".MKA" or ".MKS" or ".MK3D" or ".WEBM" => MediaFormat.Matroska,
				".MPG" or ".MPEG" or ".MPE" or ".VOB" or ".MPV" or ".M1V" or ".M2V" => MediaFormat.MpegPs,
				".AAC" => MediaFormat.Aac,
				".AVI" => MediaFormat.Avi,
				".JPG" or ".JPEG" or ".JPE" => MediaFormat.Jpeg,
				".PNG" => MediaFormat.Png,
				".GIF" => MediaFormat.Gif,
				".BMP" or ".DIB" => MediaFormat.Bmp,
				".PCX" => MediaFormat.Pcx,
				".PBM" or ".PGM" or ".PPM" or ".PNM" => MediaFormat.Netpbm,
				".TIF" or ".TIFF" => MediaFormat.Tiff,
				".DNG" => MediaFormat.Dng,
				".SVG" => MediaFormat.Svg,
				_ => MediaFormat.Unknown
			};
		}

		return MediaFormat.Unknown;
	}

	static bool IsAac (ReadOnlySpan<byte> data, Stream? stream) =>
		stream is null ? AacFile.IsValidFormat (data) : AacFile.IsValidFormat (stream);

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

		// Check for Ogg FLAC header (0x7F + "FLAC")
		if (packetData.Length >= 5 &&
			packetData[0] == OggFlacMagic[0] &&
			packetData[1] == OggFlacMagic[1] && packetData[2] == OggFlacMagic[2] &&
			packetData[3] == OggFlacMagic[3] && packetData[4] == OggFlacMagic[4])
			return MediaFormat.OggFlac;

		// Unknown Ogg codec - default to Vorbis for backwards compatibility
		return MediaFormat.OggVorbis;
	}

	static MediaFileResult OpenFlac (ReadOnlyMemory<byte> data)
	{
		var result = FlacFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Tag, MediaFormat.Flac);
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

		return MediaFileResult.Success (result.File!, result.File!.Tag, MediaFormat.Mp3);
	}

	static MediaFileResult OpenWav (ReadOnlyMemory<byte> data)
	{
		var result = WavFile.Read (data.Span);
		if (!result.IsSuccess || result.File is null)
			return MediaFileResult.Failure (result.Error ?? "Failed to parse WAV file");

		// Prefer ID3v2 tag, fall back to RIFF INFO tag
		Tag? tag = result.File.Id3v2Tag is not null
			? result.File.Id3v2Tag
			: result.File.InfoTag;
		return MediaFileResult.Success (result.File, tag, MediaFormat.Wav);
	}

	static MediaFileResult OpenAiff (ReadOnlyMemory<byte> data)
	{
		if (!AiffFile.TryRead (data.Span, out var file) || file is null)
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
		var result = DsfFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Id3v2Tag, MediaFormat.Dsf);
	}

	static MediaFileResult OpenDff (ReadOnlyMemory<byte> data)
	{
		var result = DffFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Id3v2Tag, MediaFormat.Dff);
	}

	static MediaFileResult OpenOggFlac (ReadOnlyMemory<byte> data)
	{
		var result = OggFlacFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.VorbisComment, MediaFormat.OggFlac);
	}

	static MediaFileResult OpenWavPack (ReadOnlyMemory<byte> data)
	{
		var result = WavPackFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.ApeTag, MediaFormat.WavPack);
	}

	static MediaFileResult OpenMonkeysAudio (ReadOnlyMemory<byte> data)
	{
		var result = MonkeysAudioFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.ApeTag, MediaFormat.MonkeysAudio);
	}

	static MediaFileResult OpenImage (ReadOnlyMemory<byte> data, MediaFormat format)
	{
		var opened = StillImageFile.Open (OwnedBytes (data), format);
		return opened.IsSuccess
			? MediaFileResult.Success (opened.File!, opened.File!.Tag, format)
			: MediaFileResult.Failure (opened.Error!);
	}

	// Reuse the array behind a memory that already owns the whole buffer. A slice is copied.
	static byte[] OwnedBytes (ReadOnlyMemory<byte> data)
	{
		if (MemoryMarshal.TryGetArray (data, out ArraySegment<byte> segment)
			&& segment.Array is byte[] array
			&& segment.Offset == 0
			&& segment.Count == array.Length
			&& data.Length == array.Length)
			return array;

		return data.ToArray ();
	}


	static MediaFileResult OpenAsf (ReadOnlyMemory<byte> data)
	{
		var result = AsfFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Tag, MediaFormat.Asf);
	}

	static MediaFileResult OpenMusepack (ReadOnlyMemory<byte> data)
	{
		var result = MusepackFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.ApeTag, MediaFormat.Musepack);
	}

	static MediaFileResult OpenMatroska (ReadOnlyMemory<byte> data)
	{
		var result = MatroskaFile.Read (data.Span);
		if (!result.IsSuccess)
			return MediaFileResult.Failure (result.Error!);

		return MediaFileResult.Success (result.File!, result.File!.Tag, MediaFormat.Matroska);
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
	Dff,

	/// <summary>
	/// Ogg FLAC audio format (FLAC encoded in Ogg container).
	/// </summary>
	OggFlac,

	/// <summary>
	/// WavPack audio format (.wv).
	/// </summary>
	WavPack,

	/// <summary>
	/// Monkey's Audio format (.ape).
	/// </summary>
	MonkeysAudio,

	/// <summary>
	/// ASF/WMA format (Windows Media Audio/Video).
	/// </summary>
	Asf,

	/// <summary>
	/// Musepack audio format (.mpc, .mp+, .mpp).
	/// </summary>
	Musepack,

	/// <summary>
	/// Matroska and WebM (.mkv, .mka, .mks, .mk3d, .webm).
	/// </summary>
	Matroska,

	/// <summary>
	/// MPEG-1 and MPEG-2 program streams (.mpg, .mpeg, .mpe, .vob) and MPEG video elementary
	/// streams (.mpv, .m1v, .m2v).
	/// </summary>
	MpegPs,

	/// <summary>
	/// Raw AAC in an ADTS or ADIF stream (.aac). AAC inside MP4 is <see cref="Mp4"/>.
	/// </summary>
	Aac,

	/// <summary>
	/// Audible audiobook (.aa). Audible .aax files are MPEG-4 and report <see cref="Mp4"/>.
	/// </summary>
	Audible,

	/// <summary>
	/// AVI (RIFF <c>AVI </c> form, including OpenDML).
	/// </summary>
	Avi,

	/// <summary>JPEG still image.</summary>
	Jpeg,

	/// <summary>PNG still image.</summary>
	Png,

	/// <summary>GIF still image.</summary>
	Gif,

	/// <summary>BMP still image.</summary>
	Bmp,

	/// <summary>PCX still image.</summary>
	Pcx,

	/// <summary>Netpbm raster (PBM, PGM, PPM, or PNM).</summary>
	Netpbm,

	/// <summary>TIFF still image.</summary>
	Tiff,

	/// <summary>DNG, a TIFF file with the DNG version tag or a .dng name.</summary>
	Dng,

	/// <summary>SVG image.</summary>
	Svg
}
