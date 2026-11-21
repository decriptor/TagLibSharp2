// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Builds MP4 files with real sample tables: an audio track whose samples sit in mdat, an optional
/// QuickTime chapter text track, and an optional Nero chpl box. Offsets are file positions.
/// </summary>
internal static class Mp4ChapterTestBuilder
{
	internal const uint AudioTrackId = 1;
	internal const uint TextTrackId = 2;
	internal const uint ImageTrackId = 3;
	internal const uint UrlTrackId = 4;
	internal const uint MovieTimescale = 1000;
	internal const uint MovieDuration = 10_000;
	internal const int AudioSampleCount = 8;
	internal const int AudioSampleSize = 97;

	internal sealed class Options
	{
		/// <summary>moov before mdat (fast start) instead of after it.</summary>
		public bool MoovFirst { get; set; }

		/// <summary>chpl entries (start, title), or null for no chpl.</summary>
		public (TimeSpan Start, string Title)[]? Chpl { get; set; }

		public byte ChplVersion { get; set; } = 1;

		/// <summary>Text track samples (title, duration in <see cref="TrackTimescale"/>), or null for no track.</summary>
		public (string Title, uint Duration)[]? Track { get; set; }

		public uint TrackTimescale { get; set; } = 600;

		/// <summary>Write the text samples as UTF-16BE with a byte order mark.</summary>
		public bool Utf16 { get; set; }

		/// <summary>Raw text sample bytes, replacing the rendered titles.</summary>
		public byte[][]? RawSamples { get; set; }

		/// <summary>An empty edit of this many movie units in front of the text track.</summary>
		public uint EmptyEdit { get; set; }

		/// <summary>Add moov/mvex (a fragmented movie).</summary>
		public bool Mvex { get; set; }

		/// <summary>The handler type of the text track.</summary>
		public string TextHandler { get; set; } = "text";

		/// <summary>Extra track ids for the audio track's chap reference, after the text track.</summary>
		public uint[] ExtraChapReferences { get; set; } = [];

		/// <summary>An ilst title.</summary>
		public string? Title { get; set; }

		/// <summary>Overrides the text track's chunk offset.</summary>
		public uint? TextChunkOffset { get; set; }

		/// <summary>Write the text track's chunk offset as co64.</summary>
		public bool TextCo64 { get; set; }

		/// <summary>Add a video track (id 3) that the audio track's chap names, as Apple chapter images are.</summary>
		public bool ImageTrack { get; set; }

		/// <summary>Add a second text track (id 4) that the audio track's chap names, as Apple chapter URLs are.</summary>
		public bool UrlTrack { get; set; }

		/// <summary>The ftyp major brand.</summary>
		public string MajorBrand { get; set; } = "M4A ";

		/// <summary>The text track's packed mdhd language.</summary>
		public ushort TextLanguage { get; set; } = 0x55C4;

		/// <summary>The text track's hdlr name.</summary>
		public string TextHandlerName { get; set; } = "";

		/// <summary>The text track's tkhd flags.</summary>
		public uint TextTkhdFlags { get; set; } = 2;

		/// <summary>
		/// Put the text track's samples in a movie fragment (moov gets mvex, the text track an empty stbl),
		/// located the way <see cref="FragmentBase"/> says. Null keeps them in moov.
		/// </summary>
		public FragmentBase? Fragment { get; set; }

		/// <summary>The text traf's tfdt baseMediaDecodeTime.</summary>
		public ulong FragmentDecodeTime { get; set; }
	}

	/// <summary>How a test traf locates its data (ISO 14496-12 §8.8.7).</summary>
	internal enum FragmentBase
	{
		/// <summary>tfhd default-base-is-moof; trun data_offset from the moof.</summary>
		Moof,

		/// <summary>tfhd base_data_offset; trun without data_offset.</summary>
		Explicit,

		/// <summary>No base flags, after an audio traf: the base is the end of the previous traf's data.</summary>
		PreviousTraf,
	}

	internal static byte[] AudioPayload ()
	{
		var payload = new byte[AudioSampleCount * AudioSampleSize];
		for (var i = 0; i < payload.Length; i++)
			payload[i] = (byte)((i * 31) + 7);
		return payload;
	}

	internal static byte[] Build (Options options)
	{
		if (options.Fragment is not null)
			return BuildFragmented (options);

		var audio = AudioPayload ();
		var text = TextPayload (options, out var textSizes);
		var ftyp = Mp4TestBuilder.CreateFtypBox (options.MajorBrand, 0, options.MajorBrand, "isom");

		var draft = Moov (options, 0, 0, textSizes);
		var mdatStart = ftyp.Length + (options.MoovFirst ? draft.Length : 0);
		var audioAt = (uint)(mdatStart + 8);
		var textAt = options.TextChunkOffset ?? (uint)(audioAt + audio.Length);
		var moov = Moov (options, audioAt, textAt, textSizes);

		var mdat = Mp4TestBuilder.CreateBox ("mdat", [.. audio, .. text]);
		using var file = new BinaryDataBuilder ();
		file.Add (ftyp);
		if (options.MoovFirst) {
			file.Add (moov);
			file.Add (mdat);
		} else {
			file.Add (mdat);
			file.Add (moov);
		}

		return file.ToArray ();
	}

	/// <summary>
	/// ftyp | mdat (audio) | moov (mvhd duration 0, as ffmpeg's empty_moov writes it; mvex) | moof | mdat.
	/// </summary>
	static byte[] BuildFragmented (Options options)
	{
		var audio = AudioPayload ();
		var text = TextPayload (options, out var textSizes);
		var durations = options.Track?.Select (t => t.Duration).ToArray () ?? [.. textSizes.Select (_ => 600u)];
		var ftyp = Mp4TestBuilder.CreateFtypBox (options.MajorBrand, 0, options.MajorBrand, "iso6");
		var audioMdat = Mp4TestBuilder.CreateBox ("mdat", audio);
		var audioAt = (uint)(ftyp.Length + 8);
		var moov = Moov (options, audioAt, 0, textSizes);
		var moofStart = ftyp.Length + audioMdat.Length + moov.Length;
		byte[] audioFragment = options.Fragment == FragmentBase.PreviousTraf ? new byte[20] : [];

		var draft = Moof (options.Fragment!.Value, 0, 0, textSizes, durations, options.FragmentDecodeTime);
		var fragmentData = moofStart + draft.Length + 8;
		var moof = Moof (options.Fragment.Value, moofStart, fragmentData, textSizes, durations, options.FragmentDecodeTime);
		return [.. ftyp, .. audioMdat, .. moov, .. moof, .. Mp4TestBuilder.CreateBox ("mdat", [.. audioFragment, .. text])];
	}

	static byte[] Moof (FragmentBase mode, int moofStart, int dataStart, uint[] sizes, uint[] durations, ulong decodeTime)
	{
		var trafs = new List<byte> ();
		var textStart = dataStart;
		if (mode == FragmentBase.PreviousTraf) {
			// Audio: base is the moof (first traf), data_offset to the fragment mdat, two 10-byte samples.
			trafs.AddRange (Mp4TestBuilder.CreateBox ("traf", [
				.. Mp4TestBuilder.CreateFullBox ("tfhd", 0, 0, U32 (AudioTrackId)),
				.. Mp4TestBuilder.CreateFullBox ("trun", 0, 0x201, [.. U32 (2), .. U32 ((uint)(dataStart - moofStart)), .. U32 (10), .. U32 (10)]),
			]));
			textStart += 20;
		}

		var entries = new List<byte> ();
		for (var i = 0; i < sizes.Length; i++)
			entries.AddRange ([.. U32 (durations[i]), .. U32 (sizes[i])]);

		var (tfhdFlags, tfhdBody, trunFlags, trunOffset) = mode switch {
			FragmentBase.Moof => (0x020000u, U32 (TextTrackId), 0x301u, U32 ((uint)(textStart - moofStart))),
			FragmentBase.Explicit => (0x1u, [.. U32 (TextTrackId), .. U64 ((ulong)textStart)], 0x300u, Array.Empty<byte> ()),
			_ => (0u, U32 (TextTrackId), 0x300u, Array.Empty<byte> ()),
		};
		trafs.AddRange (Mp4TestBuilder.CreateBox ("traf", [
			.. Mp4TestBuilder.CreateFullBox ("tfhd", 0, tfhdFlags, tfhdBody),
			.. Mp4TestBuilder.CreateFullBox ("tfdt", 1, 0, U64 (decodeTime)),
			.. Mp4TestBuilder.CreateFullBox ("trun", 0, trunFlags, [.. U32 ((uint)sizes.Length), .. trunOffset, .. entries]),
		]));

		return Mp4TestBuilder.CreateBox ("moof", [.. Mp4TestBuilder.CreateFullBox ("mfhd", 0, 0, U32 (1)), .. trafs]);
	}

	static byte[] U32 (uint value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian (bytes, value);
		return bytes;
	}

	static byte[] U64 (ulong value)
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian (bytes, value);
		return bytes;
	}

	static byte[] TextPayload (Options options, out uint[] sizes)
	{
		if (options.RawSamples is not null) {
			sizes = [.. options.RawSamples.Select (s => (uint)s.Length)];
			return [.. options.RawSamples.SelectMany (s => s)];
		}

		if (options.Track is null) {
			sizes = [];
			return [];
		}

		using var data = new BinaryDataBuilder ();
		sizes = new uint[options.Track.Length];
		for (var i = 0; i < options.Track.Length; i++) {
			var start = data.Length;
			var bytes = options.Utf16
				? [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes (options.Track[i].Title)]
				: Encoding.UTF8.GetBytes (options.Track[i].Title);
			data.AddUInt16BE ((ushort)bytes.Length);
			data.Add (bytes);
			data.Add (Mp4TestBuilder.CreateBox ("encd", [0, 0, 1, 0]));
			sizes[i] = (uint)(data.Length - start);
		}

		return data.ToArray ();
	}

	static byte[] Moov (Options options, uint audioAt, uint textAt, uint[] textSizes)
	{
		var hasTrack = options.Track is not null || options.RawSamples is not null;
		var children = new List<byte[]> { Mvhd (options.UrlTrack ? 5u : options.ImageTrack ? 4u : hasTrack ? 3u : 2u, options.Fragment is null ? MovieDuration : 0) };
		uint[] extra = [
			.. options.ExtraChapReferences,
			.. options.ImageTrack ? [ImageTrackId] : Array.Empty<uint> (),
			.. options.UrlTrack ? [UrlTrackId] : Array.Empty<uint> (),
		];
		uint[] chap = hasTrack ? [TextTrackId, .. extra] : extra;
		children.Add (AudioTrak (audioAt, chap));
		if (hasTrack) {
			var durations = options.Track?.Select (t => t.Duration).ToArray ()
				?? [.. textSizes.Select (_ => 600u)];
			children.Add (TextTrak (options, textAt, textSizes, durations));
		}

		if (options.ImageTrack)
			children.Add (EmptyTrak (ImageTrackId, "vide"));
		if (options.UrlTrack)
			children.Add (EmptyTrak (UrlTrackId, "text"));

		if (options.Fragment is not null) {
			byte[] Trex (uint id) => Mp4TestBuilder.CreateFullBox ("trex", 0, 0, [.. U32 (id), .. U32 (1), .. new byte[12]]);
			children.Add (Mp4TestBuilder.CreateBox ("mvex", [.. Trex (AudioTrackId), .. Trex (TextTrackId)]));
		} else if (options.Mvex) {
			byte[] Trex (uint id) => Mp4TestBuilder.CreateFullBox ("trex", 0, 0, [.. U32 (id), .. U32 (1), .. new byte[12]]);
			children.Add (Mp4TestBuilder.CreateBox ("mvex", [.. Trex (AudioTrackId), .. (hasTrack ? Trex (TextTrackId) : [])]));
		}

		var udta = new List<byte[]> ();
		if (options.Title is not null)
			udta.Add (Mp4TestBuilder.CreateBox ("meta", [0, 0, 0, 0, .. MetaChildren (options.Title)]));
		if (options.Chpl is not null)
			udta.Add (Chpl (options.Chpl, options.ChplVersion));
		if (udta.Count > 0)
			children.Add (Mp4TestBuilder.CreateBox ("udta", [.. udta.SelectMany (b => b)]));

		return Mp4TestBuilder.CreateMoovBox ([.. children]);
	}

	static byte[] MetaChildren (string title)
	{
		var hdlr = Mp4TestBuilder.CreateFullBox ("hdlr", 0, 0, [0, 0, 0, 0, .. "mdir"u8, .. "appl"u8, .. new byte[9]]);
		// data: type indicator 1 (UTF-8), locale 0, then the text.
		var data = Mp4TestBuilder.CreateBox ("data", [0, 0, 0, 1, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes (title)]);
		// The item type has a non-ASCII byte, so it is written as Latin-1.
		byte[] item = [0, 0, 0, (byte)(8 + data.Length), 0xA9, (byte)'n', (byte)'a', (byte)'m', .. data];
		var ilst = Mp4TestBuilder.CreateBox ("ilst", item);
		return [.. hdlr, .. ilst];
	}

	internal static byte[] Chpl ((TimeSpan Start, string Title)[] entries, byte version = 1)
	{
		using var body = new BinaryDataBuilder ();
		if (version == 1)
			body.AddUInt32BE (0);
		body.Add ((byte)entries.Length);
		foreach (var (start, title) in entries) {
			var bytes = Encoding.UTF8.GetBytes (title);
			body.AddUInt64BE ((ulong)start.Ticks);
			body.Add ((byte)bytes.Length);
			body.Add (bytes);
		}

		return Mp4TestBuilder.CreateFullBox ("chpl", version, 0, body.ToArray ());
	}

	static byte[] Mvhd (uint nextTrackId, uint duration)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		body.AddUInt32BE (MovieTimescale);
		body.AddUInt32BE (duration);
		body.AddUInt32BE (0x00010000);
		body.AddUInt16BE (0x0100);
		body.AddZeros (10);
		AddMatrix (body);
		body.AddZeros (24);
		body.AddUInt32BE (nextTrackId);
		return Mp4TestBuilder.CreateFullBox ("mvhd", 0, 0, body.ToArray ());
	}

	static byte[] Tkhd (uint trackId, uint flags, uint duration)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		body.AddUInt32BE (trackId);
		body.AddUInt32BE (0);
		body.AddUInt32BE (duration);
		body.AddZeros (8);
		body.AddZeros (8);
		AddMatrix (body);
		body.AddZeros (8);
		return Mp4TestBuilder.CreateFullBox ("tkhd", 0, flags, body.ToArray ());
	}

	static byte[] Mdhd (uint timescale, uint duration, ushort language = 0x55C4)
	{
		using var body = new BinaryDataBuilder ();
		body.AddUInt32BE (0);
		body.AddUInt32BE (0);
		body.AddUInt32BE (timescale);
		body.AddUInt32BE (duration);
		body.AddUInt16BE (language);
		body.AddUInt16BE (0);
		return Mp4TestBuilder.CreateFullBox ("mdhd", 0, 0, body.ToArray ());
	}

	static byte[] Hdlr (string type, string name = "") =>
		Mp4TestBuilder.CreateFullBox ("hdlr", 0, 0, [0, 0, 0, 0, .. Encoding.ASCII.GetBytes (type), .. new byte[12], .. Encoding.UTF8.GetBytes (name), 0]);

	static byte[] Dinf ()
	{
		var url = Mp4TestBuilder.CreateFullBox ("url ", 0, 1, []);
		var dref = Mp4TestBuilder.CreateFullBox ("dref", 0, 0, [0, 0, 0, 1, .. url]);
		return Mp4TestBuilder.CreateBox ("dinf", dref);
	}

	static byte[] Table (string type, params uint[] values)
	{
		using var body = new BinaryDataBuilder ();
		foreach (var value in values)
			body.AddUInt32BE (value);
		return Mp4TestBuilder.CreateFullBox (type, 0, 0, body.ToArray ());
	}

	static byte[] AudioTrak (uint chunkOffset, uint[] chap)
	{
		var entry = new BinaryDataBuilder ();
		entry.AddZeros (6);
		entry.AddUInt16BE (1);
		entry.AddZeros (8);
		entry.AddUInt16BE (2);
		entry.AddUInt16BE (16);
		entry.AddZeros (4);
		entry.AddUInt32BE (44100u << 16);
		var stsd = Mp4TestBuilder.CreateFullBox ("stsd", 0, 0, [0, 0, 0, 1, .. Mp4TestBuilder.CreateBox ("mp4a", entry.ToArray ())]);
		var stsz = Table ("stsz", [0, AudioSampleCount, .. Enumerable.Repeat ((uint)AudioSampleSize, AudioSampleCount)]);
		var stbl = Mp4TestBuilder.CreateBox ("stbl", [
			.. stsd,
			.. Table ("stts", 1, AudioSampleCount, 1250),
			.. Table ("stsc", 1, 1, AudioSampleCount, 1),
			.. stsz,
			.. Table ("stco", 1, chunkOffset),
		]);
		var smhd = Mp4TestBuilder.CreateFullBox ("smhd", 0, 0, [0, 0, 0, 0]);
		var minf = Mp4TestBuilder.CreateBox ("minf", [.. smhd, .. Dinf (), .. stbl]);
		var mdia = Mp4TestBuilder.CreateBox ("mdia", [.. Mdhd (1000, MovieDuration), .. Hdlr ("soun"), .. minf]);

		var children = new List<byte> ();
		children.AddRange (Tkhd (AudioTrackId, 3, MovieDuration));
		if (chap.Length > 0) {
			var ids = chap.SelectMany (id => new[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id });
			children.AddRange (Mp4TestBuilder.CreateBox ("tref", Mp4TestBuilder.CreateBox ("chap", [.. ids])));
		}

		children.AddRange (mdia);
		return Mp4TestBuilder.CreateBox ("trak", [.. children]);
	}

	static byte[] TextTrak (Options options, uint chunkOffset, uint[] sizes, uint[] durations)
	{
		var entry = new byte[8];
		BinaryPrimitives.WriteUInt16BigEndian (entry.AsSpan (6), 1);
		var stsd = Mp4TestBuilder.CreateFullBox ("stsd", 0, 0, [0, 0, 0, 1, .. Mp4TestBuilder.CreateBox ("text", entry)]);
		var stts = new List<uint> { (uint)durations.Length };
		foreach (var duration in durations) {
			stts.Add (1);
			stts.Add (duration);
		}

		// In a fragmented movie the samples are in the moof, and moov's tables are empty, as ffmpeg writes them.
		var stbl = options.Fragment is not null
			? Mp4TestBuilder.CreateBox ("stbl", [.. stsd, .. Table ("stts", 0), .. Table ("stsc", 0), .. Table ("stsz", 0, 0), .. Table ("stco", 0)])
			: Mp4TestBuilder.CreateBox ("stbl", [
				.. stsd,
				.. Table ("stts", [.. stts]),
				.. Table ("stsc", 1, 1, (uint)sizes.Length, 1),
				.. Table ("stsz", [0, (uint)sizes.Length, .. sizes]),
				.. (options.TextCo64 ? Table ("co64", 1, 0, chunkOffset) : Table ("stco", 1, chunkOffset)),
			]);
		var nmhd = Mp4TestBuilder.CreateFullBox ("nmhd", 0, 0, []);
		var minf = Mp4TestBuilder.CreateBox ("minf", [.. nmhd, .. Dinf (), .. stbl]);
		var total = (uint)durations.Sum (d => (long)d);
		var mdia = Mp4TestBuilder.CreateBox ("mdia", [
			.. Mdhd (options.TrackTimescale, options.Fragment is null ? total : 0, options.TextLanguage),
			.. Hdlr (options.TextHandler, options.TextHandlerName),
			.. minf,
		]);

		var children = new List<byte> ();
		children.AddRange (Tkhd (TextTrackId, options.TextTkhdFlags, MovieDuration));
		if (options.EmptyEdit > 0) {
			var elst = Table ("elst", 2, options.EmptyEdit, 0xFFFFFFFF, 0x00010000, MovieDuration - options.EmptyEdit, 0, 0x00010000);
			children.AddRange (Mp4TestBuilder.CreateBox ("edts", elst));
		}

		children.AddRange (mdia);
		return Mp4TestBuilder.CreateBox ("trak", [.. children]);
	}

	static byte[] EmptyTrak (uint trackId, string handler)
	{
		var stbl = Mp4TestBuilder.CreateBox ("stbl", [
			.. Mp4TestBuilder.CreateFullBox ("stsd", 0, 0, [0, 0, 0, 0]),
			.. Table ("stts", 0),
			.. Table ("stsc", 0),
			.. Table ("stsz", 0, 0),
			.. Table ("stco", 0),
		]);
		var minf = Mp4TestBuilder.CreateBox ("minf", [.. Dinf (), .. stbl]);
		var mdia = Mp4TestBuilder.CreateBox ("mdia", [.. Mdhd (1000, MovieDuration), .. Hdlr (handler), .. minf]);
		return Mp4TestBuilder.CreateBox ("trak", [.. Tkhd (trackId, 2, MovieDuration), .. mdia]);
	}

	static void AddMatrix (BinaryDataBuilder body)
	{
		body.AddUInt32BE (0x00010000);
		body.AddZeros (12);
		body.AddUInt32BE (0x00010000);
		body.AddZeros (12);
		body.AddUInt32BE (0x40000000);
	}

	// ---- inspection

	internal static Mp4Box Moov (byte[] file)
	{
		var offset = 0;
		while (offset < file.Length) {
			var result = Mp4Box.Parse (file.AsSpan (offset));
			Assert.IsTrue (result.IsSuccess, $"top-level box at {offset} parses");
			if (result.Box!.Type == "moov")
				return result.Box;
			offset += result.BytesConsumed;
		}

		Assert.Fail ("no moov");
		return null!;
	}

	internal static List<string> TopLevelTypes (byte[] file)
	{
		var types = new List<string> ();
		var offset = 0;
		while (offset < file.Length) {
			var result = Mp4Box.Parse (file.AsSpan (offset));
			Assert.IsTrue (result.IsSuccess, $"top-level box at {offset} parses");
			types.Add (result.Box!.Type);
			offset += result.BytesConsumed;
		}

		return types;
	}

	internal static Mp4Box? Trak (Mp4Box moov, uint trackId)
	{
		foreach (var trak in moov.Children.Where (c => c.Type == "trak")) {
			var tkhd = trak.FindChild ("tkhd")!.Data.Span;
			if (BinaryPrimitives.ReadUInt32BigEndian (tkhd.Slice (12)) == trackId)
				return trak;
		}

		return null;
	}

	internal static uint[] TrackIds (Mp4Box moov) =>
		[.. moov.Children.Where (c => c.Type == "trak").Select (t => BinaryPrimitives.ReadUInt32BigEndian (t.FindChild ("tkhd")!.Data.Span.Slice (12)))];

	internal static uint[] ChapReferences (Mp4Box trak)
	{
		var tref = trak.FindChild ("tref");
		if (tref is null)
			return [];

		var ids = new List<uint> ();
		var data = tref.Data.Span;
		for (var pos = 0; pos + 8 <= data.Length;) {
			var size = (int)BinaryPrimitives.ReadUInt32BigEndian (data.Slice (pos));
			if (data.Slice (pos + 4, 4).SequenceEqual ("chap"u8)) {
				for (var at = pos + 8; at + 4 <= pos + size; at += 4)
					ids.Add (BinaryPrimitives.ReadUInt32BigEndian (data.Slice (at)));
			}

			pos += size;
		}

		return [.. ids];
	}

	/// <summary>
	/// The bytes of every sample of <paramref name="trackId"/>, gathered through its stco/stsc/stsz.
	/// </summary>
	internal static byte[] SampleBytes (byte[] file, uint trackId)
	{
		var stbl = Trak (Moov (file), trackId)!.FindDescendant ("mdia", "minf", "stbl")!;
		var stsz = stbl.FindChild ("stsz")!.Data.Span;
		var stco = stbl.FindChild ("stco")!.Data.Span;
		var stsc = stbl.FindChild ("stsc")!.Data.Span;
		var count = BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (8));
		var chunks = BinaryPrimitives.ReadUInt32BigEndian (stco.Slice (4));
		Assert.AreEqual (1u, BinaryPrimitives.ReadUInt32BigEndian (stsc.Slice (4)), "one stsc row");
		Assert.AreEqual (1u, chunks, "one chunk");
		var offset = (int)BinaryPrimitives.ReadUInt32BigEndian (stco.Slice (8));
		var total = 0;
		for (var i = 0; i < count; i++)
			total += (int)BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (12 + (i * 4)));
		return file.AsSpan (offset, total).ToArray ();
	}

	internal static Mp4File ReadFile (byte[] data)
	{
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return result.File!;
	}
}
