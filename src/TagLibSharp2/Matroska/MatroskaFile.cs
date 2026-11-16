// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

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

	MatroskaFile (MatroskaTag tag, TimeSpan? duration, MediaTypes mediaTypes)
	{
		Tag = tag;
		Duration = duration;
		MediaTypes = mediaTypes;
	}

	/// <summary>Gets the Matroska tags.</summary>
	public MatroskaTag Tag { get; }

	Tag? IMediaFile.Tag => Tag;

	/// <summary>Gets the segment duration, when <c>Info/Duration</c> is present.</summary>
	public TimeSpan? Duration { get; }

	/// <summary>Gets audio and video flags from <c>TrackType</c>.</summary>
	public MediaTypes MediaTypes { get; }

	IMediaProperties? IMediaFile.AudioProperties =>
		(MediaTypes & MediaTypes.Audio) != 0 && Duration is TimeSpan duration
			? new AudioProperties (duration, 0, 0, 0, 0, "Matroska")
			: null;

	VideoProperties? IMediaFile.VideoProperties =>
		(MediaTypes & MediaTypes.Video) != 0 && Duration is TimeSpan duration
			? new VideoProperties (duration, 0, 0, 0, 0, "Matroska")
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
		var mediaTypes = MediaTypes.None;
		var segmentEnd = segment.UnknownSize ? data.Length : segment.End;
		offset = segment.DataOffset;
		while (offset < segmentEnd) {
			if (!EbmlReader.TryRead (data, offset, segmentEnd, out var element))
				break;

			if (element.Id == MatroskaIds.Info)
				ReadInfo (data, Range (data, element, segmentEnd), ref scale, ref durationTicks, tag);
			else if (element.Id == MatroskaIds.Tracks)
				mediaTypes |= ReadTrackTypes (data, Range (data, element, segmentEnd));
			else if (element.Id == MatroskaIds.Tags)
				ReadTags (data, Range (data, element, segmentEnd), tag);

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

		return MatroskaFileReadResult.Success (new MatroskaFile (tag, duration, mediaTypes));
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

	static MediaTypes ReadTrackTypes (ReadOnlySpan<byte> data, ReadOnlySpan<byte> body)
	{
		var types = MediaTypes.None;
		var offset = 0;
		while (offset < body.Length) {
			if (!EbmlReader.TryRead (body, offset, body.Length, out var element))
				break;
			if (element.Id == MatroskaIds.TrackEntry && !element.UnknownSize)
				types |= ReadTrackType (body.Slice (element.DataOffset, (int)element.DataLength));
			if (element.UnknownSize)
				break;
			offset = element.End;
		}

		return types;
	}

	static MediaTypes ReadTrackType (ReadOnlySpan<byte> entry)
	{
		var offset = 0;
		while (offset < entry.Length) {
			if (!EbmlReader.TryRead (entry, offset, entry.Length, out var element) || element.UnknownSize)
				break;
			if (element.Id == MatroskaIds.TrackType) {
				var kind = EbmlReader.ReadUInt (entry.Slice (element.DataOffset, (int)element.DataLength));
				if (kind == 1)
					return MediaTypes.Video;
				if (kind == 2)
					return MediaTypes.Audio;
				return MediaTypes.None;
			}

			offset = element.End;
		}

		return MediaTypes.None;
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
