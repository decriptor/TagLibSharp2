// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// Result of reading a Matroska or WebM file.
/// </summary>
public readonly struct MatroskaFileReadResult : IEquatable<MatroskaFileReadResult>
{
	MatroskaFileReadResult (MatroskaFile? file, string? error)
	{
		File = file;
		Error = error;
	}

	/// <summary>Gets the parsed file, or null when reading failed.</summary>
	public MatroskaFile? File { get; }

	/// <summary>Gets the error, or null when reading succeeded.</summary>
	public string? Error { get; }

	/// <summary>Gets a value indicating whether a file was parsed.</summary>
	public bool IsSuccess => File is not null && Error is null;

	/// <summary>Creates a successful result.</summary>
	public static MatroskaFileReadResult Success (MatroskaFile file) => new (file, null);

	/// <summary>Creates a failed result.</summary>
	public static MatroskaFileReadResult Failure (string error) => new (null, error);

	/// <inheritdoc/>
	public bool Equals (MatroskaFileReadResult other) => Equals (File, other.File) && Error == other.Error;

	/// <inheritdoc/>
	public override bool Equals (object? obj) => obj is MatroskaFileReadResult other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (File, Error);

	/// <summary>Compares two results.</summary>
	public static bool operator == (MatroskaFileReadResult left, MatroskaFileReadResult right) => left.Equals (right);

	/// <summary>Compares two results.</summary>
	public static bool operator != (MatroskaFileReadResult left, MatroskaFileReadResult right) => !left.Equals (right);
}

/// <summary>
/// A Matroska or WebM file. Reading maps <c>Tags</c> onto <see cref="Tag"/>.
/// Rewriting the container is not implemented: a short or shifted element corrupts the file.
/// </summary>
public sealed class MatroskaFile : IMediaFile
{
	const ulong DefaultTimestampScale = 1_000_000;

	MatroskaFile (MatroskaTag tag, TimeSpan? duration, TrackLayout layout)
	{
		Tag = tag;
		Duration = duration;
		MediaTypes = layout.Types;
		_sampleRate = layout.SampleRate;
		_channels = layout.Channels;
		_bitsPerSample = layout.BitsPerSample;
		_audioCodec = layout.AudioCodec;
		_width = layout.Width;
		_height = layout.Height;
		_frameRate = layout.FrameRate;
		_videoCodec = layout.VideoCodec;
	}

	readonly int _sampleRate;
	readonly int _channels;
	readonly int _bitsPerSample;
	readonly string? _audioCodec;
	readonly int _width;
	readonly int _height;
	readonly double _frameRate;
	readonly string? _videoCodec;

	/// <summary>Gets the Matroska tags.</summary>
	public MatroskaTag Tag { get; }

	Tag? IMediaFile.Tag => Tag;

	/// <summary>Gets the segment duration, when <c>Info/Duration</c> is present.</summary>
	public TimeSpan? Duration { get; }

	/// <summary>Gets audio and video flags from <c>TrackType</c>.</summary>
	public MediaTypes MediaTypes { get; }

	IMediaProperties? IMediaFile.AudioProperties =>
		(MediaTypes & MediaTypes.Audio) != 0
			? new AudioProperties (Duration ?? TimeSpan.Zero, 0, _sampleRate, _bitsPerSample, _channels, _audioCodec ?? "Matroska")
			: null;

	VideoProperties? IMediaFile.VideoProperties =>
		(MediaTypes & MediaTypes.Video) != 0
			? new VideoProperties (Duration ?? TimeSpan.Zero, _width, _height, 0, _frameRate, _videoCodec ?? "Matroska")
			: null;

	ImageProperties? IMediaFile.ImageProperties => null;

	/// <summary>Gets the path passed to a file read, when one was used.</summary>
	public string? SourcePath { get; private set; }

	/// <summary>Gets <see cref="MediaFormat.Matroska"/>.</summary>
	public MediaFormat Format => MediaFormat.Matroska;

	/// <summary>Returns true when the data starts with the EBML magic.</summary>
	public static bool IsValidFormat (ReadOnlySpan<byte> data) =>
		data.Length >= 4
		&& data[0] == 0x1A
		&& data[1] == 0x45
		&& data[2] == 0xDF
		&& data[3] == 0xA3;

	/// <summary>Reads tags and segment duration from a Matroska or WebM file.</summary>
	public static MatroskaFileReadResult Read (ReadOnlySpan<byte> data)
	{
		if (!IsValidFormat (data))
			return MatroskaFileReadResult.Failure ("Not a Matroska file: missing EBML header");

		if (!EbmlReader.TryRead (data, 0, data.Length, out var header) || header.Id != MatroskaIds.Ebml || header.UnknownSize)
			return MatroskaFileReadResult.Failure ("Not a Matroska file: EBML header is truncated");

		var docType = ReadDocType (data, header);
		if (docType is null)
			return MatroskaFileReadResult.Failure ("Not a Matroska file: DocType is missing");
		if (!docType.Equals ("matroska", StringComparison.OrdinalIgnoreCase)
			&& !docType.Equals ("webm", StringComparison.OrdinalIgnoreCase))
			return MatroskaFileReadResult.Failure ("Not a Matroska file: DocType is " + docType);

		var offset = header.End;
		EbmlElement segment = default;
		var foundSegment = false;
		while (offset < data.Length) {
			if (!EbmlReader.TryRead (data, offset, data.Length, out var element))
				break;
			if (element.Id == MatroskaIds.Segment) {
				segment = element;
				foundSegment = true;
				break;
			}

			if (element.UnknownSize)
				return MatroskaFileReadResult.Failure ("Matroska element size is unknown before Segment");
			offset = element.End;
		}

		if (!foundSegment)
			return MatroskaFileReadResult.Failure ("Matroska file has no Segment");

		var tag = new MatroskaTag ();
		var scale = DefaultTimestampScale;
		double? durationTicks = null;
		var layout = new TrackLayout ();
		var segmentEnd = segment.UnknownSize ? data.Length : segment.End;
		offset = segment.DataOffset;
		while (offset < segmentEnd) {
			// Padding and a short illegal ID are skipped. A live segment can put
			// 0x0A bytes between an unknown size and the first real element.
			if (!EbmlReader.TryRead (data, offset, segmentEnd, out var element)) {
				offset++;
				continue;
			}

			if (element.Id == MatroskaIds.Info)
				ReadInfo (data, Range (data, element, segmentEnd), ref scale, ref durationTicks, tag);
			else if (element.Id == MatroskaIds.Tracks)
				ReadTracks (Range (data, element, segmentEnd), ref layout);
			else if (element.Id == MatroskaIds.Tags)
				ReadTags (data, Range (data, element, segmentEnd), tag);
			else if (element.Id == MatroskaIds.Attachments)
				ReadAttachments (Range (data, element, segmentEnd), tag);

			if (element.UnknownSize)
				offset = NextSibling (data, element.DataOffset, segmentEnd);
			else
				offset = element.End;
		}

		TimeSpan? duration = null;
		if (durationTicks is double ticks && scale > 0 && !double.IsNaN (ticks) && !double.IsInfinity (ticks)) {
			var seconds = ticks * scale / 1_000_000_000d;
			if (seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
				duration = TimeSpan.FromSeconds (seconds);
		}

		return MatroskaFileReadResult.Success (new MatroskaFile (tag, duration, layout));
	}

	/// <summary>Reads a file without an error string.</summary>
	public static bool TryRead (ReadOnlySpan<byte> data, out MatroskaFile? file)
	{
		var result = Read (data);
		file = result.File;
		return result.IsSuccess;
	}

	/// <inheritdoc/>
	public void Dispose ()
	{
	}

	internal void SetSourcePath (string? path) => SourcePath = path;

	static string? ReadDocType (ReadOnlySpan<byte> data, EbmlElement header)
	{
		var offset = header.DataOffset;
		var end = header.End;
		while (offset < end) {
			if (!EbmlReader.TryRead (data, offset, end, out var element) || element.UnknownSize)
				break;
			if (element.Id == MatroskaIds.DocType)
				return EbmlReader.ReadString (data.Slice (element.DataOffset, (int)element.DataLength));
			offset = element.End;
		}

		return null;
	}

	static void ReadInfo (ReadOnlySpan<byte> data, ReadOnlySpan<byte> body, ref ulong scale, ref double? durationTicks, MatroskaTag tag)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TimestampScale && payload.Length > 0)
				scale = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.Duration && EbmlReader.TryReadFloat (payload, out var duration))
				durationTicks = duration;
			else if (element.Id == MatroskaIds.SegmentTitle)
				tag.SetSegmentTitle (EbmlReader.ReadString (payload));
			offset = element.End;
		}
	}

	static void ReadTracks (ReadOnlySpan<byte> body, ref TrackLayout layout)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.TrackEntry && !element.UnknownSize)
				ReadTrackEntry (body.Slice (element.DataOffset, (int)element.DataLength), ref layout);
			if (element.UnknownSize)
				break;
			offset = element.End;
		}
	}

	static void ReadTrackEntry (ReadOnlySpan<byte> entry, ref TrackLayout layout)
	{
		ulong kind = 0;
		string? codecId = null;
		string? codecName = null;
		var isDefault = true;
		var privateOffset = -1;
		var privateLength = 0;
		var rate = 0;
		var channels = 0;
		var bits = 0;
		var width = 0;
		var height = 0;
		double frameRate = 0;
		var offset = 0;
		while (offset < entry.Length) {
			if (!EbmlReader.TryRead (entry, offset, entry.Length, out var element) || element.UnknownSize)
				break;
			var payload = entry.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TrackType)
				kind = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.FlagDefault)
				isDefault = payload.Length == 0 || EbmlReader.ReadUInt (payload) != 0;
			else if (element.Id == MatroskaIds.CodecId)
				codecId = EbmlReader.ReadString (payload).TrimEnd ('\0');
			else if (element.Id == MatroskaIds.CodecPrivate) {
				privateOffset = element.DataOffset;
				privateLength = (int)element.DataLength;
			} else if (element.Id == MatroskaIds.CodecName)
				codecName = EbmlReader.ReadString (payload).TrimEnd ('\0');
			else if (element.Id == MatroskaIds.DefaultDuration && payload.Length > 0) {
				var nanos = EbmlReader.ReadUInt (payload);
				if (nanos > 0)
					frameRate = 1_000_000_000d / nanos;
			} else if (element.Id == MatroskaIds.Audio)
				ReadAudio (payload, ref rate, ref channels, ref bits);
			else if (element.Id == MatroskaIds.Video)
				ReadVideoSize (payload, ref width, ref height);
			offset = element.End;
		}

		var codecPrivate = privateOffset >= 0
			? entry.Slice (privateOffset, privateLength)
			: ReadOnlySpan<byte>.Empty;
		var codec = CodecLabel (codecId, codecName, codecPrivate);
		// FlagDefault defaults to 1. Keep the first default track. A later default replaces one that opted out.
		if (kind == 2) {
			layout.Types |= MediaTypes.Audio;
			if (!layout.HasAudio || (!layout.AudioIsDefault && isDefault)) {
				layout.HasAudio = true;
				layout.AudioIsDefault = isDefault;
				layout.SampleRate = rate;
				layout.Channels = channels;
				layout.BitsPerSample = bits;
				layout.AudioCodec = codec;
			}
		} else if (kind == 1) {
			layout.Types |= MediaTypes.Video;
			if (!layout.HasVideo || (!layout.VideoIsDefault && isDefault)) {
				layout.HasVideo = true;
				layout.VideoIsDefault = isDefault;
				layout.Width = width;
				layout.Height = height;
				layout.FrameRate = frameRate;
				layout.VideoCodec = codec;
			}
		}
	}

	static void ReadAudio (ReadOnlySpan<byte> body, ref int rate, ref int channels, ref int bits)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.SamplingFrequency && EbmlReader.TryReadFloat (payload, out var frequency) && frequency > 0)
				rate = (int)frequency;
			else if (element.Id == MatroskaIds.Channels && payload.Length > 0)
				channels = (int)EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.BitDepth && payload.Length > 0)
				bits = (int)EbmlReader.ReadUInt (payload);
			offset = element.End;
		}

		// The Channels element defaults to 1 when an Audio element omits it.
		if (channels == 0)
			channels = 1;
	}

	static void ReadVideoSize (ReadOnlySpan<byte> body, ref int width, ref int height)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.PixelWidth)
				width = (int)EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.PixelHeight)
				height = (int)EbmlReader.ReadUInt (payload);
			offset = element.End;
		}
	}

	static string CodecLabel (string? codecId, string? codecName, ReadOnlySpan<byte> codecPrivate)
	{
		if (!string.IsNullOrEmpty (codecName) && codecName != codecId)
			return codecName;
		if (codecId == "V_MS/VFW/FOURCC" && TryVfwFourCc (codecPrivate, out var fourcc))
			return fourcc;
		return codecId switch {
			"A_VORBIS" => "Vorbis",
			"A_OPUS" => "Opus",
			"A_AAC" or "A_AAC/MPEG2/LC" or "A_AAC/MPEG4/LC" => "AAC",
			"A_MPEG/L3" => "MP3",
			"A_FLAC" => "FLAC",
			"A_PCM/INT/LIT" or "A_PCM/INT/BIG" => "PCM",
			"A_AC3" => "AC-3",
			"A_DTS" => "DTS",
			"V_VP8" => "VP8",
			"V_VP9" => "VP9",
			"V_AV1" => "AV1",
			"V_MPEG4/ISO/AVC" => "H.264",
			"V_MPEGH/ISO/HEVC" => "HEVC",
			"V_THEORA" => "Theora",
			_ => string.IsNullOrEmpty (codecId) ? "Matroska" : codecId
		};
	}

	/// <summary>
	/// <c>V_MS/VFW/FOURCC</c> stores a BITMAPINFOHEADER. <c>biCompression</c> at offset 16 is the fourcc.
	/// </summary>
	static bool TryVfwFourCc (ReadOnlySpan<byte> header, out string name)
	{
		name = string.Empty;
		if (header.Length < 20)
			return false;
		var biSize = BinaryPrimitives.ReadUInt32LittleEndian (header);
		if (biSize < 20 || biSize > (uint)header.Length)
			return false;

		for (var i = 0; i < 4; i++) {
			var b = header[16 + i];
			if (b < 0x20 || b > 0x7E)
				return false;
		}

		var bytes = new byte[4];
		header.Slice (16, 4).CopyTo (bytes);
		var fourcc = Encoding.ASCII.GetString (bytes).Trim ();
		if (fourcc.Length == 0)
			return false;

		name = fourcc switch {
			"MP41" or "MP42" or "MP43" or "DIVX" or "DX50" or "XVID" or "FMP4" or "MP4V" or "M4S2" or "DIV3" or "3IV2" => "MPEG-4 Visual",
			_ => fourcc
		};
		return true;
	}

	static void ReadAttachments (ReadOnlySpan<byte> body, MatroskaTag tag)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.AttachedFile && !element.UnknownSize)
				ReadAttachedFile (body.Slice (element.DataOffset, (int)element.DataLength), tag);
			if (element.UnknownSize)
				break;
			offset = element.End;
		}
	}

	static void ReadAttachedFile (ReadOnlySpan<byte> body, MatroskaTag tag)
	{
		string? name = null;
		string? description = null;
		string? mime = null;
		byte[]? bytes = null;
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.FileName)
				name = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.FileDescription)
				description = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.FileMimeType)
				mime = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.FileData && payload.Length > 0)
				bytes = payload.ToArray ();
			offset = element.End;
		}

		if (bytes is null || mime is null || !mime.StartsWith ("image/", StringComparison.OrdinalIgnoreCase))
			return;

		var cover = (name is not null && name.Contains ("cover", StringComparison.OrdinalIgnoreCase))
			|| (description is not null && description.Contains ("cover", StringComparison.OrdinalIgnoreCase))
			|| string.Equals (description, "Front", StringComparison.OrdinalIgnoreCase);
		tag.AddPicture (new MatroskaPicture (
			mime,
			cover ? PictureType.FrontCover : PictureType.Other,
			description ?? name ?? string.Empty,
			bytes));
	}

	struct TrackLayout
	{
		public MediaTypes Types;
		public int SampleRate;
		public int Channels;
		public int BitsPerSample;
		public string? AudioCodec;
		public bool HasAudio;
		public bool AudioIsDefault;
		public int Width;
		public int Height;
		public double FrameRate;
		public string? VideoCodec;
		public bool HasVideo;
		public bool VideoIsDefault;
	}

	static void ReadTags (ReadOnlySpan<byte> data, ReadOnlySpan<byte> body, MatroskaTag tag)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.Tag && !element.UnknownSize)
				tag.AddBlock (ReadTag (body.Slice (element.DataOffset, (int)element.DataLength)));
			if (element.UnknownSize)
				break;
			offset = element.End;
		}
	}

	static TagBlock ReadTag (ReadOnlySpan<byte> body)
	{
		var block = new TagBlock (50);
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.Targets)
				ReadTargets (payload, block);
			else if (element.Id == MatroskaIds.SimpleTag)
				block.Tags.Add (ReadSimpleTag (payload));
			offset = element.End;
		}

		return block;
	}

	static void ReadTargets (ReadOnlySpan<byte> body, TagBlock block)
	{
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TargetTypeValue && payload.Length > 0)
				block.Level = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.TagChapterUid)
				block.HasChapter = true;
			else if (element.Id == MatroskaIds.TagAttachmentUid)
				block.HasAttachment = true;
			else if (element.Id == MatroskaIds.TagTrackUid)
				block.HasTrack = true;
			else if (element.Id == MatroskaIds.TagEditionUid)
				block.HasEdition = true;
			offset = element.End;
		}
	}

	static SimpleTag ReadSimpleTag (ReadOnlySpan<byte> body)
	{
		var name = string.Empty;
		string? text = null;
		var isDefault = true;
		var children = new List<SimpleTag> ();
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element) || element.UnknownSize)
				break;
			var payload = body.Slice (element.DataOffset, (int)element.DataLength);
			if (element.Id == MatroskaIds.TagName)
				name = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.TagString)
				text = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.TagDefault || element.Id == MatroskaIds.TagDefaultBogus)
				isDefault = payload.Length == 0 || EbmlReader.ReadUInt (payload) != 0;
			else if (element.Id == MatroskaIds.SimpleTag)
				children.Add (ReadSimpleTag (payload));
			offset = element.End;
		}

		var tag = new SimpleTag (name, text, isDefault);
		foreach (var child in children)
			tag.Children.Add (child);
		return tag;
	}

	static ReadOnlySpan<byte> Range (ReadOnlySpan<byte> data, EbmlElement element, int parentEnd)
	{
		if (!element.UnknownSize)
			return data.Slice (element.DataOffset, (int)element.DataLength);

		var end = NextSibling (data, element.DataOffset, parentEnd);
		return data.Slice (element.DataOffset, end - element.DataOffset);
	}

	/// <summary>
	/// An unknown-size element ends at the next sibling ID, not at the next byte that looks like media.
	/// </summary>
	static int NextSibling (ReadOnlySpan<byte> data, int offset, int parentEnd)
	{
		while (offset < parentEnd) {
			if (!EbmlReader.TryRead (data, offset, parentEnd, out var element))
				return parentEnd;
			if (MatroskaIds.IsSegmentChild (element.Id))
				return element.Position;
			if (element.UnknownSize)
				return parentEnd;
			offset = element.End;
		}

		return parentEnd;
	}
}
