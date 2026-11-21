// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;

using TagLibSharp2.Core;

namespace TagLibSharp2.Matroska;

/// <summary>
/// The fields of one <c>AttachedFile</c> (RFC 9559 section 5.1.6.1).
/// <see cref="DataOffset"/> and <see cref="DataLength"/> are relative to the parsed body.
/// </summary>
internal readonly struct AttachedFileInfo
{
	public AttachedFileInfo (string? name, string? description, string? mediaType, int dataOffset, int dataLength, ulong? uid)
	{
		Name = name;
		Description = description;
		MediaType = mediaType;
		DataOffset = dataOffset;
		DataLength = dataLength;
		Uid = uid;
	}

	public string? Name { get; }

	public string? Description { get; }

	public string? MediaType { get; }

	/// <summary>Gets the offset of the <c>FileData</c> payload, or -1 when there is none.</summary>
	public int DataOffset { get; }

	public int DataLength { get; }

	public ulong? Uid { get; }

	/// <summary>
	/// Gets a value indicating whether the reader surfaces this attachment as a picture:
	/// an <c>image/</c> media type and a nonempty <c>FileData</c>.
	/// </summary>
	public bool IsPicture =>
		DataOffset >= 0
		&& DataLength > 0
		&& MediaType is not null
		&& MediaType.StartsWith ("image/", StringComparison.OrdinalIgnoreCase);

	/// <summary>Gets the picture description: <c>FileDescription</c>, else <c>FileName</c>.</summary>
	public string PictureDescription => Description ?? Name ?? string.Empty;

	public PictureType PictureType => MatroskaAttachments.TypeOf (Name, Description);
}

/// <summary>
/// Maps <see cref="Tag.Pictures"/> onto image attachments.
/// RFC 9559 section 21.1 and https://www.matroska.org/technical/cover_art/index.html name the covers.
/// </summary>
/// <remarks>
/// <para>
/// A front cover is written as <c>cover.jpg</c> or <c>cover.png</c> (another image type keeps its
/// extension, such as <c>cover.gif</c>) and goes first in storage order, unless the file keeps its
/// normal cover <c>cover.*</c>; then the new one goes after the kept attachments.
/// The guidance names only cover variants, so every other picture type gets the name in
/// <see cref="StemOf"/>, such as <c>back.jpg</c> or <c>artist.png</c>. None of them contain
/// "cover", so a reader that looks for cover names does not pick them. A repeated name gets
/// <c>_2</c>, <c>_3</c> and so on before the extension.
/// </para>
/// <para>
/// The description goes in <c>FileDescription</c>. Reading maps those names back to the type,
/// so a written picture keeps its type, description, media type and bytes.
/// </para>
/// <para>
/// An image attachment that matches a picture keeps its element bytes, <c>FileName</c> and <c>FileUID</c>.
/// Every other attachment, such as a font, is copied unchanged.
/// </para>
/// </remarks>
internal static class MatroskaAttachments
{
	const string FrontStem = "cover";

	/// <summary>
	/// Reads the children of an <c>AttachedFile</c>. A child that does not parse ends the read,
	/// as the reader does for every master element.
	/// </summary>
	public static AttachedFileInfo Read (ReadOnlySpan<byte> body)
	{
		string? name = null;
		string? description = null;
		string? mediaType = null;
		ulong? uid = null;
		var dataOffset = -1;
		var dataLength = 0;
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
				mediaType = EbmlReader.ReadString (payload);
			else if (element.Id == MatroskaIds.FileUid)
				uid = EbmlReader.ReadUInt (payload);
			else if (element.Id == MatroskaIds.FileData) {
				dataOffset = element.DataOffset;
				dataLength = payload.Length;
			}

			offset = element.End;
		}

		return new AttachedFileInfo (name, description, mediaType, dataOffset, dataLength, uid);
	}

	/// <summary>
	/// The picture type of an attachment. A name this writer uses maps back to its type.
	/// Otherwise "cover" in the name or description, or a "Front" description, is the front cover.
	/// </summary>
	public static PictureType TypeOf (string? name, string? description)
	{
		if (TryTypeFromName (name, out var type))
			return type;

		var cover = (name is not null && name.Contains ("cover", StringComparison.OrdinalIgnoreCase))
			|| (description is not null && description.Contains ("cover", StringComparison.OrdinalIgnoreCase))
			|| string.Equals (description, "Front", StringComparison.OrdinalIgnoreCase);
		return cover ? PictureType.FrontCover : PictureType.Other;
	}

	/// <summary>
	/// The file name stem for a picture type. RFC 9559 section 21.1 names only the covers.
	/// The guidance says file names are case-sensitive, so the match is ordinal.
	/// </summary>
	internal static string StemOf (PictureType type) => type switch {
		PictureType.FrontCover => FrontStem,
		PictureType.BackCover => "back",
		PictureType.FileIcon => "file_icon",
		PictureType.OtherFileIcon => "other_file_icon",
		PictureType.LeafletPage => "leaflet_page",
		PictureType.Media => "media",
		PictureType.LeadArtist => "lead_artist",
		PictureType.Artist => "artist",
		PictureType.Conductor => "conductor",
		PictureType.Band => "band",
		PictureType.Composer => "composer",
		PictureType.Lyricist => "lyricist",
		PictureType.RecordingLocation => "recording_location",
		PictureType.DuringRecording => "during_recording",
		PictureType.DuringPerformance => "during_performance",
		PictureType.MovieScreenCapture => "screen_capture",
		PictureType.ColouredFish => "coloured_fish",
		PictureType.Illustration => "illustration",
		PictureType.BandLogo => "band_logo",
		PictureType.PublisherLogo => "publisher_logo",
		_ => "other"
	};

	static bool TryTypeFromName (string? name, out PictureType type)
	{
		type = PictureType.Other;
		if (name is null || name.Length == 0)
			return false;

		var dot = name.LastIndexOf ('.');
		var stem = dot > 0 ? name.Substring (0, dot) : name;
		if (TryTypeFromStem (stem, out type))
			return true;

		// A repeated name is "<stem>_<n>".
		var underscore = stem.LastIndexOf ('_');
		if (underscore <= 0 || underscore == stem.Length - 1)
			return false;
		for (var i = underscore + 1; i < stem.Length; i++) {
			if (stem[i] is < '0' or > '9')
				return false;
		}

		return TryTypeFromStem (stem.Substring (0, underscore), out type);
	}

	static bool TryTypeFromStem (string stem, out PictureType type)
	{
		// The cover art guidance: small_ prefix and _land suffix are still the front cover.
		if (stem is FrontStem or "small_cover" or "cover_land" or "small_cover_land") {
			type = PictureType.FrontCover;
			return true;
		}

		for (var value = 0; value <= (int)PictureType.PublisherLogo; value++) {
			var candidate = (PictureType)value;
			if (candidate != PictureType.FrontCover && string.Equals (StemOf (candidate), stem, StringComparison.Ordinal)) {
				type = candidate;
				return true;
			}
		}

		type = PictureType.Other;
		return false;
	}

	/// <summary>The extension for a media type. RFC 9559 section 21.1 asks for JPEG or PNG covers.</summary>
	internal static string ExtensionOf (string mediaType)
	{
		// The subtype, lower case, letters and digits only: "image/svg+xml" is "svgxml".
		var chars = new System.Text.StringBuilder ();
		var slash = false;
		foreach (var raw in mediaType) {
			if (!slash) {
				slash = raw == '/';
				continue;
			}

			if (raw == ';' || chars.Length == 16)
				break;
			var c = raw is >= 'A' and <= 'Z' ? (char)(raw + ('a' - 'A')) : raw;
			if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
				chars.Append (c);
		}

		var subtype = chars.ToString ();
		return subtype switch {
			"jpeg" or "jpg" or "pjpeg" => "jpg",
			"svgxml" => "svg",
			"" => "img",
			_ => subtype
		};
	}

	/// <summary>
	/// The media type to write for a picture. An empty type is detected from the image bytes.
	/// </summary>
	/// <returns>The media type, or null when it is not an <c>image/</c> type or is not printable ASCII.</returns>
	internal static string? MediaTypeOf (IPicture picture)
	{
		var mediaType = picture.MimeType?.Trim ();
		if (string.IsNullOrEmpty (mediaType))
			mediaType = Picture.DetectMimeType (picture.PictureData.Span);

		// FileMediaType is an EBML string: printable ASCII (RFC 8794 section 7.4).
		if (mediaType!.Length <= "image/".Length || !mediaType.StartsWith ("image/", StringComparison.OrdinalIgnoreCase))
			return null;
		foreach (var c in mediaType) {
			if (c is < ' ' or > '~')
				return null;
		}

		return mediaType;
	}

	/// <summary>
	/// Builds the <c>Attachments</c> element for <paramref name="pictures"/>.
	/// <paramref name="ranges"/> are the existing <c>Attachments</c> elements in <paramref name="data"/>.
	/// </summary>
	/// <param name="data">The file.</param>
	/// <param name="ranges">The existing <c>Attachments</c> elements, offset and length, in file order.</param>
	/// <param name="pictures">The pictures to store.</param>
	/// <param name="webm">True for a WebM file, which has no <c>Attachments</c> element.</param>
	/// <param name="element">The new element, or empty when no attachment is left.</param>
	/// <param name="changed">False when every picture is an existing image attachment and none was removed.</param>
	/// <param name="removed">The <c>FileUID</c> of every image attachment that was removed.</param>
	/// <param name="error">Why the pictures cannot be written.</param>
	public static bool TryBuild (
		ReadOnlySpan<byte> data,
		IReadOnlyList<(int Offset, int Length)> ranges,
		IReadOnlyList<IPicture> pictures,
		bool webm,
		out byte[] element,
		out bool changed,
		out HashSet<ulong> removed,
		out string error)
	{
		element = [];
		changed = false;
		removed = new HashSet<ulong> ();
		if (!TryCollect (data, ranges, out var existing, out var hadCrc, out error))
			return false;

		var matched = new bool[existing.Count];
		var added = new List<(IPicture Picture, string MediaType)> ();
		for (var p = 0; p < pictures.Count; p++) {
			var picture = pictures[p];

			// A picture read from this file matches its own attachment with the media type as stored.
			// The reader surfaces any image/ type, so only a picture that will be written is validated.
			var stored = picture.MimeType ?? string.Empty;
			var index = FindMatch (data, existing, matched, picture, stored);
			var mediaType = index >= 0 ? stored : MediaTypeOf (picture);
			if (index < 0 && mediaType is not null && !string.Equals (mediaType, stored, StringComparison.OrdinalIgnoreCase))
				index = FindMatch (data, existing, matched, picture, mediaType);
			if (index >= 0) {
				matched[index] = true;
				continue;
			}

			if (mediaType is null) {
				error = "Matroska picture " + p + " is not an image/ media type; only images can be written as pictures";
				return false;
			}

			if (picture.PictureData.Length == 0) {
				error = "Matroska picture " + p + " has no data";
				return false;
			}

			added.Add ((picture, mediaType));
		}

		var usedUids = new HashSet<ulong> ();
		var usedNames = new HashSet<string> (StringComparer.Ordinal);
		var keptFront = false;
		long capacity = 16;
		for (var i = 0; i < existing.Count; i++) {
			var entry = existing[i];
			if (entry.Info.Uid is ulong uid)
				usedUids.Add (uid);
			if (entry.IsPicture && !matched[i]) {
				changed = true;
				if (entry.Info.Uid is ulong gone)
					removed.Add (gone);
				continue;
			}

			if (entry.Info.Name is not null)
				usedNames.Add (entry.Info.Name);
			if (entry.IsPicture && IsNormalCover (entry.Info.Name))
				keptFront = true;
			capacity += entry.Length;
		}

		foreach (var (picture, mediaType) in added)
			capacity += picture.PictureData.Length + Encoding.UTF8.GetByteCount (picture.Description ?? string.Empty) + mediaType.Length + 96;

		if (added.Count > 0)
			changed = true;
		if (!changed)
			return true;

		// WebM does not define Attachments. https://www.webmproject.org/docs/container/
		if (webm && added.Count > 0) {
			error = "WebM does not support attachments; pictures cannot be written to a WebM file";
			return false;
		}

		// Sized from what is written, not from the file: the Clusters are not copied here.
		using var body = new BinaryDataBuilder ((int)Math.Min (int.MaxValue, capacity));
		var attachedFiles = 0;

		// The normal cover SHOULD be the first attachment in storage order (RFC 9559 section 21.1).
		// A kept normal cover is already that cover, so a new one goes after the kept attachments.
		foreach (var (picture, mediaType) in added) {
			if (picture.PictureType == PictureType.FrontCover && !keptFront) {
				AddAttachedFile (body, picture, mediaType, usedNames, usedUids);
				attachedFiles++;
			}
		}

		for (var i = 0; i < existing.Count; i++) {
			var entry = existing[i];
			if (entry.IsPicture && !matched[i])
				continue;
			body.Add (data.Slice (entry.Offset, entry.Length));
			if (entry.IsAttachedFile)
				attachedFiles++;
		}

		foreach (var (picture, mediaType) in added) {
			if (picture.PictureType != PictureType.FrontCover || keptFront) {
				AddAttachedFile (body, picture, mediaType, usedNames, usedUids);
				attachedFiles++;
			}
		}

		// Attachments needs at least one AttachedFile. With none left the element goes.
		if (attachedFiles == 0)
			return true;

		element = AttachmentsElement (body.Span, hadCrc);
		return true;
	}

	/// <summary>
	/// The normal cover is <c>cover.(jpg|png)</c> (RFC 9559 section 21.1). The <c>small_</c> and
	/// <c>_land</c> variants are other covers, and file names are case-sensitive.
	/// </summary>
	static bool IsNormalCover (string? name)
	{
		if (name is null)
			return false;
		var dot = name.LastIndexOf ('.');
		return dot == FrontStem.Length && string.CompareOrdinal (name, 0, FrontStem, 0, FrontStem.Length) == 0;
	}

	static int FindMatch (ReadOnlySpan<byte> data, List<Entry> existing, bool[] matched, IPicture picture, string mediaType)
	{
		var bytes = picture.PictureData.Span;
		for (var i = 0; i < existing.Count; i++) {
			var entry = existing[i];
			if (matched[i] || !entry.IsPicture)
				continue;
			var info = entry.Info;
			if (!string.Equals (info.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
				continue;
			if (info.PictureType != picture.PictureType)
				continue;
			var description = picture.Description ?? string.Empty;
			// An attachment without FileDescription reads its name as the description.
			if (description != info.PictureDescription && !(description.Length == 0 && info.Description is null))
				continue;
			if (!data.Slice (entry.Offset + entry.BodyOffset + info.DataOffset, info.DataLength).SequenceEqual (bytes))
				continue;
			return i;
		}

		return -1;
	}

	/// <summary>Writes one <c>AttachedFile</c>; the picture bytes are copied once.</summary>
	static void AddAttachedFile (BinaryDataBuilder output, IPicture picture, string mediaType, HashSet<string> usedNames, HashSet<ulong> usedUids)
	{
		var stem = StemOf (picture.PictureType);
		var extension = ExtensionOf (mediaType);
		var name = stem + "." + extension;
		for (var n = 2; usedNames.Contains (name); n++)
			name = stem + "_" + n + "." + extension;
		usedNames.Add (name);

		var uid = NewUid (usedUids);
		var bytes = picture.PictureData.Span;
		using var head = new BinaryDataBuilder (256);
		var description = picture.Description ?? string.Empty;
		if (description.Length > 0 && description != name)
			head.Add (EbmlWriter.String (MatroskaIds.FileDescription, description));
		head.Add (EbmlWriter.String (MatroskaIds.FileName, name));
		head.Add (EbmlWriter.String (MatroskaIds.FileMimeType, mediaType));
		head.Add (EbmlWriter.Unsigned (MatroskaIds.FileUid, uid));
		head.Add (FileDataId);
		head.Add (EbmlWriter.Size (bytes.Length));

		output.Add (AttachedFileId);
		output.Add (EbmlWriter.Size (head.Length + bytes.Length));
		output.Add (head.Span);
		output.Add (bytes);
	}

	static readonly byte[] AttachedFileId = [0x61, 0xA7];
	static readonly byte[] FileDataId = [0x46, 0x5C];
	static readonly byte[] AttachmentsId = [0x19, 0x41, 0xA4, 0x69];

	/// <summary>
	/// FileUID: "not 0", "as random as possible" (RFC 9559 section 5.1.6.1.5), and unlike every
	/// FileUID already in the file. Track, chapter and edition UIDs are separate spaces.
	/// </summary>
	static ulong NewUid (HashSet<ulong> used)
	{
		var bytes = new byte[8];
		using var random = RandomNumberGenerator.Create ();
		while (true) {
			random.GetBytes (bytes);
			ulong uid = 0;
			for (var i = 0; i < bytes.Length; i++)
				uid = (uid << 8) | bytes[i];
			if (uid != 0 && used.Add (uid))
				return uid;
		}
	}

	/// <summary>
	/// The <c>Attachments</c> element around <paramref name="payload"/>, built in one copy.
	/// A CRC-32 that led the old element leads the new one (RFC 9559 section 6.2).
	/// It covers the rest of the payload, little-endian (RFC 8794 section 11.3.1).
	/// </summary>
	static byte[] AttachmentsElement (ReadOnlySpan<byte> payload, bool crc)
	{
		var crcLength = crc ? 6 : 0;
		var size = EbmlWriter.Size (crcLength + payload.Length);
		var element = new byte[AttachmentsId.Length + size.Length + crcLength + payload.Length];
		AttachmentsId.CopyTo (element, 0);
		size.CopyTo (element, AttachmentsId.Length);
		var at = AttachmentsId.Length + size.Length;
		if (crc) {
			var value = MatroskaRewrite.Crc32 (payload);
			element[at] = 0xBF;
			element[at + 1] = 0x84;
			element[at + 2] = (byte)value;
			element[at + 3] = (byte)(value >> 8);
			element[at + 4] = (byte)(value >> 16);
			element[at + 5] = (byte)(value >> 24);
			at += crcLength;
		}

		payload.CopyTo (element.AsSpan (at));
		return element;
	}

	/// <summary>
	/// The children of every <c>Attachments</c> element in file order. CRC-32 and Void are dropped
	/// and rebuilt. A child that does not parse fails the save, so no attachment is lost.
	/// </summary>
	static bool TryCollect (
		ReadOnlySpan<byte> data,
		IReadOnlyList<(int Offset, int Length)> ranges,
		out List<Entry> entries,
		out bool hadCrc,
		out string error)
	{
		entries = new List<Entry> ();
		hadCrc = false;
		error = "";
		for (var r = 0; r < ranges.Count; r++) {
			var (start, length) = ranges[r];
			var end = start + length;
			if (!EbmlReader.TryRead (data, start, end, out var attachments) || attachments.Id != MatroskaIds.Attachments) {
				error = "Matroska Attachments cannot be bounded";
				return false;
			}

			var offset = attachments.DataOffset;
			var first = true;
			while (offset < end) {
				if (!EbmlReader.TryRead (data, offset, end, out var child) || child.UnknownSize) {
					error = "Matroska Attachments cannot be bounded";
					return false;
				}

				if (child.Id == MatroskaIds.Crc32) {
					if (first && r == 0)
						hadCrc = true;
				} else if (child.Id != MatroskaIds.Void) {
					var isFile = child.Id == MatroskaIds.AttachedFile;
					var info = isFile
						? Read (data.Slice (child.DataOffset, (int)child.DataLength))
						: default;
					entries.Add (new Entry (child.Position, child.End - child.Position, child.HeaderLength, isFile, info));
				}

				first = false;
				offset = child.End;
			}
		}

		return true;
	}

	readonly struct Entry
	{
		public Entry (int offset, int length, int bodyOffset, bool isAttachedFile, AttachedFileInfo info)
		{
			Offset = offset;
			Length = length;
			BodyOffset = bodyOffset;
			IsAttachedFile = isAttachedFile;
			Info = info;
		}

		public int Offset { get; }

		public int Length { get; }

		/// <summary>Gets the header length, so the payload starts at <see cref="Offset"/> plus this.</summary>
		public int BodyOffset { get; }

		public bool IsAttachedFile { get; }

		public AttachedFileInfo Info { get; }

		public bool IsPicture => IsAttachedFile && Info.IsPicture;
	}
}
