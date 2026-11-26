// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using System.Xml.Linq;
using TagLibSharp2.Core;
using TagLibSharp2.Image;
using TagLibSharp2.Image.Exif;
using TagLibSharp2.Image.Tiff;
using TagLibSharp2.Tests.Core;
using static TagLibSharp2.Tests.Image.GainMapTestData;
using static TagLibSharp2.Tests.Image.ImageTestBuilder;

namespace TagLibSharp2.Tests.Image;

/// <summary>Clear of a version-65536 Apple gain map keeps the maker-note headroom ImageIO applies, chosen as ImageIO chooses it, in linear work.</summary>
[TestClass]
[TestCategory ("Unit")]
public class AppleHeadroomTests
{
	// Which occurrence wins, at every level the headroom passes through: IFD0's Exif IFD pointer (0x8769), the
	// Exif IFD's MakerNote (0x927C) and the maker note's 0x0021 and 0x0030. TIFF 6.0 does not say, so the rule is
	// ImageIO's, measured on m1-real variants (one level changed at a time; ImageIO HDR max: the real values
	// render 4.0031, the other valid ones 2.9742, SDR is 0.9996, "none" is no HDR image at all):
	//   level      kind                          ImageIO                   libultrahdr
	//   0x8769     LONG / IFD type, count 1      usable, later overrides   reads the first
	//              other type, count 0 or 2      skipped                   skipped
	//              table outside the data        skipped                   refuses the file
	//              later Exif IFD w/o maker note keeps the earlier values  -
	//   0x927C     UNDEFINED, > 4 bytes, inside  usable, later overrides   reads the first
	//              other type, count 0, <= 4,    skipped                   -
	//              outside the data
	//              Apple header not exact, or    skipped                   -
	//              its table does not fit
	//              not an Apple maker note       usable: wins with nothing refuses
	//   0x21/0x30  SRATIONAL, count >= 1, every  usable, the last wins     the last (out-of-note: refuses)
	//              value inside the note (any
	//              offset: header, table, end)
	//              other type, count 0, outside  skipped                   -
	//              winner with count 2           none                      reads the first value
	// So: every occurrence that cannot be used is skipped, the last usable one wins, and the headroom is kept only
	// when both winning tags have count 1. Each row below is one level x one pattern x one kind of bad occurrence.
	public enum Use { Skipped, Usable, Nothing }

	// iPhone 15 and later write HDRGainMapVersion 131072 with the headroom in the gain map's own XMP; ImageIO and
	// libultrahdr scale the gain map by it, so Clear keeps it.
	[TestMethod]
	public void Clear_KeepsTheAppleHeadroom_OfAVersion131072GainMap ()
	{
		var packet = AppleGainMapXmp.Replace ("<HDRGainMap:HDRGainMapVersion>65536</HDRGainMap:HDRGainMapVersion>",
			"<HDRGainMap:HDRGainMapVersion>131072</HDRGainMap:HDRGainMapVersion><HDRGainMap:HDRGainMapHeadroom>3.500000</HDRGainMap:HDRGainMapHeadroom>", StringComparison.Ordinal);
		var input = MpfJpeg (Concat (GpsExif (), JpegXmpBytes (XmpPacket (Seq ("dc:creator", "Jane Private")))), [], Jpeg (GpsExif (), JpegXmpBytes (packet)), out _);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var apple = XDocument.Parse (Packets (file.Render ().ToArray ()).Single ().TrimStart ('\uFEFF')).Descendants (Rdf + "Description").Single ();

		Assert.AreEqual ("131072", apple.Element (AppleGainMap + "HDRGainMapVersion")?.Value);
		Assert.AreEqual ("3.500000", apple.Element (AppleGainMap + "HDRGainMapHeadroom")?.Value);
	}

	// Tags 0x0021 and 0x0030 of an Apple maker note (exiftool's HDRHeadroom and HDRGain), each one SRATIONAL.
	static readonly (int Tag, int Type, int Count, byte[] Value) Headroom = (0x0021, 10, 1, SRational (6, 5));
	static readonly (int Tag, int Type, int Count, byte[] Value) Gain = (0x0030, 10, 1, SRational (1, 2));

	// Tags an iPhone writes next to them; the ASCII one stands in for anything personal in the maker note.
	static readonly (int Tag, int Type, int Count, byte[] Value) MakerNoteInteger = (0x0001, 9, 1, [0, 0, 0, 14]);
	static readonly (int Tag, int Type, int Count, byte[] Value) MakerNoteText = (0x000B, 2, 17, Concat ("Jane Private 4711"u8.ToArray (), [0]));

	static byte[] AppleV1 (byte[] primaryExif, string? gainMapXmp = null) =>
		MpfJpeg (
			Concat (primaryExif, GpsExif (), JpegXmpBytes (XmpPacket (Seq ("dc:creator", "Jane Private")))),
			[],
			Jpeg (JpegExif (AppleExifTiff (AppleMakerNote ([Headroom, Gain]))), JpegXmpBytes (gainMapXmp ?? AppleGainMapXmp)),
			out _);

	// The Exif Clear writes for a version-65536 Apple gain map: IFD0 -> Exif IFD -> MakerNote, and in the maker
	// note only tags 0x0021 and 0x0030 with their stored values, offsets counted from the maker note.
	static byte[] ExpectedHeadroomExif (byte[] headroom, byte[] gain)
	{
		var note = AppleMakerNote ([(0x0021, 10, 1, headroom), (0x0030, 10, 1, gain)]);
		var tiff = new byte[44];
		"MM\0*"u8.CopyTo (tiff);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (4), 8);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (8), 1);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (10), 0x8769);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (12), 4);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (14), 1);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (18), 26);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (26), 1);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (28), 0x927C);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (30), 7);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (32), note.Length);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (36), 44);
		return JpegExif (Concat (tiff, note));
	}

	static List<int> ExifSegments (byte[] jpeg)
	{
		var found = new List<int> ();
		var at = 0;
		while (jpeg.AsSpan (at).IndexOf ("Exif\0\0"u8) is var next and >= 0) {
			at += next;
			found.Add (at - 4);
			at++;
		}

		return found;
	}

	// iPhone 12-14 write HDRGainMapVersion 65536 with no headroom in the gain map's XMP. ImageIO and libultrahdr
	// take it from the primary's Apple maker note, tags 0x0021 and 0x0030 (Apple, "Applying Apple HDR effect to
	// your photos"), so Clear keeps those two values in a new Exif and nothing else of it: not IFD0, GPS or the
	// other Exif tags, not the other maker-note tags, not a second Exif segment, not the gain map's own Exif.
	[TestMethod]
	[DataRow ("tags in order")]
	[DataRow ("tags out of order")]
	[DataRow ("little-endian TIFF")]
	public void Clear_OfAVersion65536AppleGainMap_KeepsOnlyTheMakerNoteHeadroom (string order)
	{
		var entries = order == "tags out of order" ? new[] { Gain, MakerNoteText, Headroom, MakerNoteInteger } : [MakerNoteInteger, MakerNoteText, Headroom, Gain];
		// Readers use the first Exif; a second one with other values is dropped with the rest.
		var second = JpegExif (AppleExifTiff (AppleMakerNote ([(0x0021, 10, 1, SRational (9, 1)), (0x0030, 10, 1, SRational (9, 1))])));
		var file = Open (AppleV1 (Concat (JpegExif (AppleExifTiff (AppleMakerNote (entries), littleEndian: order == "little-endian TIFF")), second)), "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var segments = ExifSegments (output);
		Assert.AreEqual (1, segments.Count, "one Exif, in the primary");
		var expected = ExpectedHeadroomExif (Headroom.Value, Gain.Value);
		Assert.AreEqual (114, expected.Length);
		Assert.IsTrue (segments[0] < MpfSecondImage (output));
		CollectionAssert.AreEqual (expected, output[segments[0]..(segments[0] + expected.Length)]);
		foreach (var personal in new[] { "Jane", "iPhone", "2024:05", "Canon" })
			Assert.IsTrue (output.AsSpan ().IndexOf (Encoding.ASCII.GetBytes (personal)) < 0, $"'{personal}' survived Clear");
		StringAssert.Contains (Packets (output).Single (), "65536");
		Assert.AreEqual (output.Length - MpfSecondImage (output), MpfEntries (output)[1].Size);
		Assert.IsNotNull (Open (output, "a.jpg").ImageProperties);
	}

	// Without a valid headroom in the maker note there is nothing to keep, and none of the Exif survives.
	public static IEnumerable<object[]> UnkeptHeadroom ()
	{
		static byte[] Note (params (int Tag, int Type, int Count, byte[] Value)[] entries) => AppleMakerNote (entries);
		static byte[] Patch (byte[] note, int entry, int field, int value)
		{
			var copy = (byte[])note.Clone ();
			var at = AppleMakerNoteHeader.Length + 2 + (12 * entry) + field;
			if (field == 2)
				System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (copy.AsSpan (at), (ushort)value);
			else
				System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (copy.AsSpan (at), value);
			return copy;
		}

		static byte[] Count (byte[] note, int count)
		{
			var copy = (byte[])note.Clone ();
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (copy.AsSpan (AppleMakerNoteHeader.Length), (ushort)count);
			return copy;
		}

		static byte[] Magic (byte[] tiff, byte first, byte second)
		{
			tiff[2] = first;
			tiff[3] = second;
			return tiff;
		}

		static byte[] Ifd0Count (byte[] tiff, int count)
		{
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (tiff.AsSpan (8), (ushort)count);
			return tiff;
		}

		var valid = Note (Headroom, Gain);
		yield return ["no 0x0021", JpegExif (AppleExifTiff (Note (MakerNoteText, Gain)))];
		yield return ["no 0x0030", JpegExif (AppleExifTiff (Note (MakerNoteText, Headroom)))];
		yield return ["little-endian header", JpegExif (AppleExifTiff (AppleMakerNote ([Headroom, Gain], "Apple iOS\0\0\u0001II"u8.ToArray ())))];
		yield return ["another maker's header", JpegExif (AppleExifTiff (AppleMakerNote ([Headroom, Gain], "Nikon\0\u0002\u0010\0\0MM\0\0"u8.ToArray ())))];
		yield return ["maker note shorter than its table", JpegExif (AppleExifTiff (valid, makerNoteCount: AppleMakerNoteHeader.Length + 2 + 12 + 6))];
		yield return ["maker note shorter than its header", JpegExif (AppleExifTiff (valid, makerNoteCount: 15))];
		yield return ["maker note past the Exif", JpegExif (AppleExifTiff (valid, makerNoteCount: 70_000))];
		yield return ["maker note not UNDEFINED", JpegExif (AppleExifTiff (valid, makerNoteType: 2))];
		yield return ["0x0021 value past the maker note", JpegExif (AppleExifTiff (Patch (valid, 0, 8, valid.Length - 4)))];
		yield return ["0x0021 RATIONAL", JpegExif (AppleExifTiff (Patch (valid, 0, 2, 5)))];
		yield return ["0x0030 FLOAT", JpegExif (AppleExifTiff (Patch (valid, 1, 2, 11)))];
		yield return ["0x0021 count 2", JpegExif (AppleExifTiff (Patch (valid, 0, 4, 2)))];
		yield return ["0x0030 count 0", JpegExif (AppleExifTiff (Patch (valid, 1, 4, 0)))];
		yield return ["entry count past the maker note", JpegExif (AppleExifTiff (Count (Note (MakerNoteInteger), 50)))];
		yield return ["Exif IFD past the data", JpegExif (AppleExifTiff (valid, exifIfd: 0x7FFF_0000))];
		// The last two bytes of the TIFF (the gain's denominator, 2) read as the Exif IFD's entry count.
		yield return ["Exif IFD table past the data", JpegExif (AppleExifTiff (valid, exifIfd: AppleExifTiff (valid).Length - 2))];
		yield return ["IFD0 table past the data", JpegExif (Ifd0Count (AppleExifTiff (valid), 0x0FFF))];
		yield return ["no TIFF header", JpegSegment (0xE1, Concat ("Exif\0\0XX\0*"u8.ToArray (), valid))];
		// ImageIO ignores an Exif whose TIFF magic is not 42, so it never applied this headroom.
		yield return ["big-endian magic 43", JpegExif (Magic (AppleExifTiff (valid), 0x00, 0x2B))];
		yield return ["little-endian magic 43", JpegExif (Magic (AppleExifTiff (valid, littleEndian: true), 0x2B, 0x00))];
		// The last usable 0x0021 has count 2: ImageIO then renders no HDR image at all.
		yield return ["0x0021 twice, the last of count 2", JpegExif (AppleExifTiff (Patch (Note (Headroom, (0x0021, 10, 1, SRational (7, 3)), Gain), 1, 4, 2)))];
	}

	[TestMethod]
	[DynamicData (nameof (UnkeptHeadroom))]
	public void Clear_OfAVersion65536AppleGainMap_WithoutAValidMakerNoteHeadroom_KeepsNoExif (string shape, byte[] primaryExif)
	{
		var file = Open (AppleV1 (primaryExif), "a.jpg");
		file.Tag!.Clear ();

		var save = file.SaveToFile ("/a.jpg", new MockFileSystem ());
		Assert.IsTrue (save.IsSuccess, $"{shape}: {save.Error}");
		var output = file.Render ().ToArray ();

		Assert.AreEqual (0, ExifSegments (output).Count, shape);
		StringAssert.Contains (Packets (output).Single (), "65536", shape);
	}

	// Only a version-65536 Apple gain map reads its headroom from the maker note. An iPhone 15 gain map carries it
	// in its own XMP, Ultra HDR in hdrgm, and a single image has no gain map: none of them keeps any Exif.
	[TestMethod]
	[DataRow ("version 131072 with HDRGainMapHeadroom")]
	[DataRow ("Ultra HDR")]
	[DataRow ("Apple properties without HDRGainMapVersion")]
	[DataRow ("no gain map")]
	[DataRow ("a version-65536 packet that outgrows its segment")]
	public void Clear_KeepsNoExif_UnlessAVersion65536AppleGainMapNeedsTheMakerNote (string kind)
	{
		var exif = JpegExif (AppleExifTiff (AppleMakerNote ([MakerNoteText, Headroom, Gain])));
		var input = kind switch {
			"version 131072 with HDRGainMapHeadroom" => AppleV1 (exif, AppleGainMapXmp.Replace ("<HDRGainMap:HDRGainMapVersion>65536</HDRGainMap:HDRGainMapVersion>",
				"<HDRGainMap:HDRGainMapVersion>131072</HDRGainMap:HDRGainMapVersion><HDRGainMap:HDRGainMapHeadroom>3.5</HDRGainMap:HDRGainMapHeadroom>", StringComparison.Ordinal)),
			"Ultra HDR" => AppleV1 (exif, UltraHdrGainMap),
			"a version-65536 packet that outgrows its segment" => AppleV1 (exif, OutgrowingPacket ("xmlns:G='http://ns.apple.com/HDRGainMap/1.0/' G:HDRGainMapVersion='65536'")),
			"Apple properties without HDRGainMapVersion" => AppleV1 (exif, AppleGainMapXmp.Replace ("<HDRGainMap:HDRGainMapVersion>65536</HDRGainMap:HDRGainMapVersion>", "", StringComparison.Ordinal)),
			_ => Jpeg (exif, JpegXmpBytes (AppleGainMapXmp))
		};
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		Assert.AreEqual (0, ExifSegments (output).Count, kind);
		Assert.IsTrue (output.AsSpan ().IndexOf ("Apple iOS"u8) < 0, kind);
	}

	sealed record Kind (string Name, Use Use, (int Tag, int Type, long Count, long Value) Entry);

	// Real (6/5, 1/2) and alternative (7/3, 5/4) values; the real maker note holds both pairs, at V and V + 8,
	// V + 16 and V + 24.
	static readonly byte[] Real21 = SRational (6, 5), Alt21 = SRational (7, 3), Real30 = SRational (1, 2), Alt30 = SRational (5, 4);

	// Symbolic offsets, resolved when the TIFF is laid out.
	const long RealIfd = -1, AltIfd = -2, BareIfd = -3, ForeignIfd = -4, RealNote = -5, AltNote = -6, BadHeaderNote = -7, Foreign = -8, RealNoteLength = -9, TablePast = -10;

	static byte[] MatrixExif (List<(int Tag, int Type, long Count, long Value)> ifd0, List<(int Tag, int Type, long Count, long Value)> exif, List<(int Tag, int Type, long Count, long Value)> note)
	{
		var v = 16 + (12 * note.Count) + 4;
		var noteBytes = new byte[v + 32];
		AppleMakerNoteHeader.CopyTo (noteBytes, 0);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (noteBytes.AsSpan (14), (ushort)note.Count);
		for (var n = 0; n < note.Count; n++)
			Entry (noteBytes, 16 + (12 * n), note[n].Tag, note[n].Type, note[n].Count, note[n].Value < 0 ? v + ((-note[n].Value - 100) * 8) : note[n].Value);
		Concat (Real21, Alt21, Real30, Alt30).CopyTo (noteBytes, v);

		var alt = AppleMakerNote ([(0x0021, 10, 1, Alt21), (0x0030, 10, 1, Alt30)]);
		var badHeader = (byte[])alt.Clone ();
		badHeader[12] = 2;
		// Another maker's note; read as Apple's it would claim 65,535 entries.
		var foreign = Concat ("Nikon\0\u0002\u0010\0\0MM\0*"u8.ToArray (), [0xFF, 0xFF], new byte[42], [0x0F, 0xFF]);
		var ifd0At = 8;
		var realIfd = ifd0At + 2 + (12 * ifd0.Count) + 4;
		var altIfd = realIfd + 2 + (12 * exif.Count) + 4;
		var bareIfd = altIfd + 18;
		var foreignIfd = bareIfd + 18;
		var realNote = foreignIfd + 18;
		var altNote = realNote + noteBytes.Length;
		var badHeaderNote = altNote + alt.Length;
		var foreignAt = badHeaderNote + badHeader.Length;
		var length = foreignAt + foreign.Length;
		long Resolve (long value) => value switch {
			RealIfd => realIfd,
			AltIfd => altIfd,
			BareIfd => bareIfd,
			ForeignIfd => foreignIfd,
			RealNote => realNote,
			AltNote => altNote,
			BadHeaderNote => badHeaderNote,
			Foreign => foreignAt,
			RealNoteLength => noteBytes.Length,
			TablePast => length - 2,
			_ => value
		};
		var tiff = new byte[length];
		"MM\0*"u8.CopyTo (tiff);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (4), ifd0At);
		Table (tiff, ifd0At, ifd0);
		Table (tiff, realIfd, exif);
		Table (tiff, altIfd, [(0x927C, 7, alt.Length, AltNote)]);
		Table (tiff, bareIfd, [(0x9003, 7, 4, 0x31323334)]);
		Table (tiff, foreignIfd, [(0x927C, 7, foreign.Length, Foreign)]);
		noteBytes.CopyTo (tiff, realNote);
		alt.CopyTo (tiff, altNote);
		badHeader.CopyTo (tiff, badHeaderNote);
		foreign.CopyTo (tiff, foreignAt);
		return tiff;

		void Table (byte[] data, int at, List<(int Tag, int Type, long Count, long Value)> entries)
		{
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (data.AsSpan (at), (ushort)entries.Count);
			for (var n = 0; n < entries.Count; n++)
				Entry (data, at + 2 + (12 * n), entries[n].Tag, entries[n].Type, Resolve (entries[n].Count), Resolve (entries[n].Value));
		}

		static void Entry (byte[] data, int at, int tag, int type, long count, long value)
		{
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (data.AsSpan (at), (ushort)tag);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (data.AsSpan (at + 2), (ushort)type);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (at + 4), (uint)count);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian (data.AsSpan (at + 8), (uint)value);
		}
	}

	// A maker-note value at the real note's value area: -100 is V (real 0x21), -101 V + 8, -102 V + 16, -103 V + 24.
	const long V21 = -100, VAlt21 = -101, V30 = -102, VAlt30 = -103;

	static readonly (int Tag, int Type, long Count, long Value) NoEntry = (0, 0, 0, 0);

	public static IEnumerable<object[]> OccurrenceMatrix ()
	{
		var levels = new (string Level, (int Tag, int Type, long Count, long Value) Valid, (int Tag, int Type, long Count, long Value) Other, Kind[] Kinds)[] {
			("0x8769", (0x8769, 4, 1, RealIfd), (0x8769, 13, 1, AltIfd), [
				new ("UNDEFINED", Use.Skipped, (0x8769, 7, 4, AltIfd)),
				new ("SHORT", Use.Skipped, (0x8769, 3, 1, AltIfd)),
				new ("count 0", Use.Skipped, (0x8769, 4, 0, AltIfd)),
				new ("count 2", Use.Skipped, (0x8769, 4, 2, AltIfd)),
				new ("past the data", Use.Skipped, (0x8769, 4, 1, 0xFFFFF0)),
				new ("inside the header", Use.Skipped, (0x8769, 4, 1, 4)),
				new ("table past the data", Use.Skipped, (0x8769, 4, 1, TablePast)),
				new ("an Exif IFD without a maker note", Use.Skipped, (0x8769, 4, 1, BareIfd)),
				new ("an Exif IFD with a foreign maker note", Use.Skipped, (0x8769, 4, 1, ForeignIfd))]),
			("0x927C", (0x927C, 7, RealNoteLength, RealNote), (0x927C, 7, 60, AltNote), [
				new ("ASCII", Use.Skipped, (0x927C, 2, 60, AltNote)),
				new ("count 0", Use.Skipped, (0x927C, 7, 0, AltNote)),
				new ("4 bytes, held in the entry", Use.Skipped, (0x927C, 7, 4, AltNote)),
				new ("past the data", Use.Skipped, (0x927C, 7, 60, 0xFFFFF0)),
				new ("Apple header of version 2", Use.Skipped, (0x927C, 7, 60, BadHeaderNote)),
				new ("Apple table past its bytes", Use.Skipped, (0x927C, 7, 16, AltNote)),
				new ("not an Apple maker note", Use.Nothing, (0x927C, 7, 60, Foreign)),
				new ("8 bytes of an Apple maker note", Use.Nothing, (0x927C, 7, 8, AltNote))]),
			("0x0021", (0x0021, 10, 1, V21), (0x0021, 10, 1, VAlt21), TagKinds (0x0021)),
			("0x0030", (0x0030, 10, 1, V30), (0x0030, 10, 1, VAlt30), TagKinds (0x0030)),
		};
		foreach (var (level, valid, other, kinds) in levels) {
			yield return [level, "valid only", "-", Use.Skipped, valid, other, NoEntry];
			yield return [level, "valid, then another valid", "-", Use.Skipped, valid, other, NoEntry];
			foreach (var kind in kinds) {
				foreach (var pattern in new[] { "bad, then valid", "valid, then bad", "bad only" })
					yield return [level, pattern, kind.Name, kind.Use, valid, other, kind.Entry];
			}
		}

		static Kind[] TagKinds (int tag) => [
			new ("RATIONAL", Use.Skipped, (tag, 5, 1, VAlt21)),
			new ("count 0", Use.Skipped, (tag, 10, 0, VAlt21)),
			new ("count 2", Use.Nothing, (tag, 10, 2, V21)),
			new ("past the maker note", Use.Skipped, (tag, 10, 1, 0x7FFFFFF0)),
			new ("one byte past the maker note", Use.Skipped, (tag, 10, 1, RealNoteEnd - 7)),
			new ("the maker note's last 8 bytes", Use.Usable, (tag, 10, 1, RealNoteEnd - 8)),
			new ("in the maker-note header", Use.Usable, (tag, 10, 1, 0)),
			new ("in the maker-note entry table", Use.Usable, (tag, 10, 1, 16))];
	}

	// The real note's length, resolved per note (see NoteEnd).
	const long RealNoteEnd = 1_000_000;

	[TestMethod]
	[DynamicData (nameof (OccurrenceMatrix))]
	public void Clear_KeepsTheHeadroomImageIOApplies_WhicheverOccurrenceWins (string level, string pattern, string kind, Use use, (int Tag, int Type, long Count, long Value) valid, (int Tag, int Type, long Count, long Value) other, (int Tag, int Type, long Count, long Value) bad)
	{
		var occurrences = pattern switch {
			"valid only" => [valid],
			"valid, then another valid" => [valid, other],
			"bad, then valid" => [bad, valid],
			"valid, then bad" => new[] { valid, bad },
			_ => [bad]
		};
		var ifd0 = new List<(int Tag, int Type, long Count, long Value)> { (0x010F, 2, 4, 0x41424300) };
		var exif = new List<(int Tag, int Type, long Count, long Value)> { (0x9003, 7, 4, 0x31323334) };
		var note = new List<(int Tag, int Type, long Count, long Value)> { (0x0001, 9, 1, 14) };
		var target = level switch { "0x8769" => ifd0, "0x927C" => exif, _ => note };
		if (level != "0x8769")
			ifd0.Add ((0x8769, 4, 1, RealIfd));
		if (level != "0x927C")
			exif.Add ((0x927C, 7, RealNoteLength, RealNote));
		if (level != "0x0021")
			note.Add ((0x0021, 10, 1, V21));
		if (level != "0x0030")
			note.Add ((0x0030, 10, 1, V30));
		var noteLength = 16 + (12 * (note.Count + (level is "0x0021" or "0x0030" ? occurrences.Length : 0))) + 4 + 32;
		foreach (var o in occurrences)
			target.Add (o.Value == RealNoteEnd - 7 || o.Value == RealNoteEnd - 8 ? (o.Tag, o.Type, o.Count, noteLength - (RealNoteEnd - o.Value)) : o);
		var tiff = MatrixExif (ifd0, exif, note);

		var file = Open (AppleV1 (JpegExif (tiff)), "a.jpg");
		file.Tag!.Clear ();
		var output = file.Render ().ToArray ();
		var segments = ExifSegments (output);
		var row = $"{level} / {pattern} / {kind}";

		// The winner is the last occurrence that is not skipped; none, or one that brings nothing, keeps nothing.
		var usable = occurrences.Where (o => !(o.Equals (bad) && use == Use.Skipped)).ToList ();
		var winner = usable.Count > 0 ? usable[^1] : bad;
		if (usable.Count == 0 || (winner.Equals (bad) && use == Use.Nothing)) {
			Assert.AreEqual (0, segments.Count, row);
			return;
		}

		var (headroom, gain) = (Real21, Real30);
		if (winner.Equals (other))
			(headroom, gain) = level switch { "0x0021" => (Alt21, Real30), "0x0030" => (Real21, Alt30), _ => (Alt21, Alt30) };
		if (winner.Equals (bad)) {
			// A usable entry at an odd offset: the kept bytes are the 8 there, as stored.
			var noteAt = tiff.AsSpan ().IndexOf ("Apple iOS\0"u8);
			var offset = (int)target.Last ().Value;
			var bytes = tiff[(noteAt + offset)..(noteAt + offset + 8)];
			(headroom, gain) = level == "0x0021" ? (bytes, Real30) : (Real21, bytes);
		}

		Assert.AreEqual (1, segments.Count, row);
		var expected = ExpectedHeadroomExif (headroom, gain);
		CollectionAssert.AreEqual (expected, output[segments[0]..(segments[0] + expected.Length)], row);
	}

	// Each distinct Exif IFD is walked once however many IFD0 entries point at it, so the work stays linear in the
	// entries (and IFD tables that overlap past the data keep nothing rather than being walked again and again).
	[TestMethod]
	public void Clear_ExifIfdPointerWalk_IsLinearInTheEntries ()
	{
		static long Steps (int n)
		{
			var ifd0 = Enumerable.Range (0, n).Select (_ => (Tag: 0x8769, Type: 4, Count: 1L, Value: RealIfd)).ToList ();
			var exif = Enumerable.Range (0, n).Select (k => (Tag: 0x9000 + k, Type: 7, Count: 4L, Value: 0L)).ToList ();
			exif.Add ((0x927C, 7, RealNoteLength, RealNote));
			var file = Open (AppleV1 (JpegExif (MatrixExif (ifd0, exif, [(0x0021, 10, 1, V21), (0x0030, 10, 1, V30)]))), "a.jpg");
			file.Tag!.Clear ();
			IfdSteps.Count = 0;
			var output = file.Render ().ToArray ();
			Assert.AreEqual (1, ExifSegments (output).Count, $"N={n}: the headroom is kept");
			return IfdSteps.Count;
		}

		var small = Steps (300);
		var large = Steps (2400);
		Assert.IsTrue (large >= 3L * 2400, $"{large} steps: every IFD0 entry, pointer kept and Exif IFD entry is counted");
		Assert.IsTrue (large <= 4L * 2400, $"{large} steps for 2,400 pointers to one Exif IFD of 2,400 entries");
		Assert.IsTrue (large < 10 * small, $"8x the entries took {large / (double)small:F1}x the steps");
	}

	// Many pointers to one Exif IFD whose maker note lacks the tags, after an Exif IFD that has them: the shared
	// Exif IFD is walked once, so the earlier values are still found (walking it again for each pointer would
	// exceed the entries the data holds and keep nothing).
	[TestMethod]
	public void Clear_ManyPointersToOneExifIfd_WalkItOnce ()
	{
		var ifd0 = new List<(int Tag, int Type, long Count, long Value)> { (0x8769, 4, 1, AltIfd) };
		ifd0.AddRange (Enumerable.Range (0, 200).Select (_ => (0x8769, 4, 1L, RealIfd)));
		var exif = Enumerable.Range (0, 200).Select (k => (Tag: 0x9000 + k, Type: 7, Count: 4L, Value: 0L)).ToList ();
		exif.Add ((0x927C, 7, RealNoteLength, RealNote));
		var file = Open (AppleV1 (JpegExif (MatrixExif (ifd0, exif, [(0x0001, 9, 1, 14)]))), "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var segments = ExifSegments (output);
		Assert.AreEqual (1, segments.Count);
		var expected = ExpectedHeadroomExif (Alt21, Alt30);
		CollectionAssert.AreEqual (expected, output[segments[0]..(segments[0] + expected.Length)]);
	}

	// Exif IFDs or maker notes that overlap, so that walking each would read the same bytes again and again, keep
	// nothing once the entries walked exceed what the data holds.
	[TestMethod]
	[DataRow ("400 Exif IFDs two bytes apart")]
	[DataRow ("300 Exif IFDs sharing one maker note without the tags")]
	public void Clear_OverlappingExifIfdsOrMakerNotes_AreBoundedByTheData (string shape)
	{
		var ifd0 = new List<(int Tag, int Type, long Count, long Value)> ();
		var exif = new List<(int Tag, int Type, long Count, long Value)> ();
		var note = new List<(int Tag, int Type, long Count, long Value)> ();
		byte[] tiff;
		if (shape.StartsWith ("400", StringComparison.Ordinal)) {
			// Every pointer lands in a run of 0x00 0xC8 bytes: each reads as a table of 200 entries.
			ifd0.AddRange (Enumerable.Range (0, 400).Select (n => (0x8769, 4, 1L, 4820L + (2 * n))));
			tiff = MatrixExif (ifd0, exif, [(0x0021, 10, 1, V21), (0x0030, 10, 1, V30)]);
			tiff = Concat (tiff[..4820], Enumerable.Repeat (new byte[] { 0x00, 0xC8 }, 2200).SelectMany (b => b).ToArray ());
		} else {
			note.AddRange (Enumerable.Range (0, 2000).Select (k => (0x1000 + k, 9, 1L, 0L)));
			exif.Add ((0x927C, 7, RealNoteLength, RealNote));
			ifd0.AddRange (Enumerable.Range (0, 301).Select (n => (0x8769, 4, 1L, n == 0 ? RealIfd : 0L)));
			var head = MatrixExif (ifd0, exif, note);

			// 300 more Exif IFDs, each a copy of the real one's table, so each names the same maker note.
			var realIfd = 8 + 2 + (12 * 301) + 4;
			var table = head[realIfd..(realIfd + 18)];
			tiff = Concat (head, Enumerable.Repeat (table, 300).SelectMany (t => t).ToArray ());
			for (var n = 1; n <= 300; n++)
				System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (tiff.AsSpan (8 + 2 + (12 * n) + 8), head.Length + (18 * (n - 1)));
		}

		IfdSteps.Count = 0;
		var kept = AppleHeadroom.Keep (tiff);

		Assert.IsNull (kept, shape);
		Assert.IsTrue (IfdSteps.Count <= tiff.Length, $"{shape}: {IfdSteps.Count} steps for {tiff.Length} bytes");
	}

	// ImageIO reads an IFD0 or Exif IFD whose 4-byte next-IFD offset is missing or cut short (m1-real with
	// either table last renders HDR, max 4.0031, as m1-real does), so Clear keeps the headroom from it.
	[TestMethod]
	[DataRow ("Exif IFD", 4)]
	[DataRow ("Exif IFD", 2)]
	[DataRow ("IFD0", 4)]
	[DataRow ("IFD0", 2)]
	public void Clear_KeepsTheHeadroom_WhenATableHasNoNextOffset (string last, int cut)
	{
		var note = AppleMakerNote ([MakerNoteText, Headroom, Gain]);
		static byte[] Table (int tag, int type, int count, int value, bool next)
		{
			var table = new byte[2 + 12 + (next ? 4 : 0)];
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (table, 1);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (table.AsSpan (2), (ushort)tag);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian (table.AsSpan (4), (ushort)type);
			System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (table.AsSpan (6), count);
			System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (table.AsSpan (10), value);
			return table;
		}

		static byte[] Header (int ifd0)
		{
			var header = "MM\0*\0\0\0\0"u8.ToArray ();
			System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian (header.AsSpan (4), ifd0);
			return header;
		}

		var partial = new byte[4 - cut];
		byte[] tiff;
		if (last == "Exif IFD") {
			// Header, IFD0 at 8, the maker note at 26, the Exif IFD last.
			var noteAt = 8 + 18;
			var exifAt = noteAt + note.Length;
			tiff = Concat (Header (8), Table (0x8769, 4, 1, exifAt, next: true), note, Table (0x927C, 7, note.Length, noteAt, next: false), partial);
		} else {
			// Header, the maker note at 8, the Exif IFD, IFD0 last.
			var exifAt = 8 + note.Length;
			var ifd0At = exifAt + 18;
			tiff = Concat (Header (ifd0At), note, Table (0x927C, 7, note.Length, 8, next: true), Table (0x8769, 4, 1, exifAt, next: false), partial);
		}

		var file = Open (AppleV1 (JpegExif (tiff)), "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var segments = ExifSegments (output);
		Assert.AreEqual (1, segments.Count, $"{last}, {cut} bytes cut");
		var expected = ExpectedHeadroomExif (SRational (6, 5), SRational (1, 2));
		CollectionAssert.AreEqual (expected, output[segments[0]..(segments[0] + expected.Length)]);
	}

	// The gain map need not be the last image the MPF index lists: any later image's packet decides.
	[TestMethod]
	[DataRow ("gain map before another image")]
	[DataRow ("gain map after another image")]
	public void Clear_KeepsTheMakerNoteHeadroom_WhereverTheGainMapIsInTheIndex (string order)
	{
		var gainMap = Jpeg (JpegXmpBytes (AppleGainMapXmp));
		var other = Jpeg (JpegSegment (0xFE, "depth"u8.ToArray ()));
		var primary = Concat (JpegExif (AppleExifTiff (AppleMakerNote ([MakerNoteText, Headroom, Gain]))), JpegXmpBytes (XmpPacket (Seq ("dc:creator", "Jane Private"))));
		var input = order == "gain map before another image" ? MpfJpegMany (primary, [], gainMap, other) : MpfJpegMany (primary, [], other, gainMap);
		var file = Open (input, "a.jpg");
		file.Tag!.Clear ();

		var output = file.Render ().ToArray ();

		var segments = ExifSegments (output);
		Assert.AreEqual (1, segments.Count, order);
		var expected = ExpectedHeadroomExif (Headroom.Value, Gain.Value);
		CollectionAssert.AreEqual (expected, output[segments[0]..(segments[0] + expected.Length)], order);
	}

	// The maker-note walk visits each entry once (counted in IfdSteps.Count), so the work Clear does is
	// linear in the entries a hostile maker note declares.
	[TestMethod]
	public void Clear_MakerNoteHeadroomWalk_IsLinearInTheEntries ()
	{
		static long Steps (int n)
		{
			var entries = Enumerable.Range (0, n).Select (k => (0x1000 + k, 9, 1, new byte[] { 0, 0, 0, 1 })).Concat ([Headroom, Gain]).ToList ();
			var file = Open (AppleV1 (JpegExif (AppleExifTiff (AppleMakerNote (entries)))), "a.jpg");
			file.Tag!.Clear ();
			IfdSteps.Count = 0;
			var output = file.Render ().ToArray ();
			Assert.AreEqual (1, ExifSegments (output).Count, $"N={n}: the headroom is kept");
			return IfdSteps.Count;
		}

		var small = Steps (500);
		var large = Steps (4000);
		Assert.IsTrue (large >= 4002, $"{large} steps: every maker-note entry is counted");
		Assert.IsTrue (large <= 2L * 4002, $"{large} steps for 4,002 entries");
		Assert.IsTrue (large < 10 * small, $"8x the entries took {large / (double)small:F1}x the steps");
	}
	// IFD0 with one Exif IFD pointer per Exif IFD, in order; each Exif IFD with one MakerNote entry per note, in order;
	// the notes after the tables. Big-endian.
	static byte[] ExifWithNotes (params byte[][][] notesOfEachExifIfd)
	{
		var m = notesOfEachExifIfd.Length;
		var at = 8 + 2 + (12 * m) + 4;
		var ifds = new List<int> ();
		foreach (var notes in notesOfEachExifIfd) {
			ifds.Add (at);
			at += 2 + (12 * notes.Length) + 4;
		}

		var length = at + notesOfEachExifIfd.Sum (notes => notes.Sum (n => n.Length));
		var tiff = new TiffBuilder (length, bigEndian: true).Header (8).Table (8, [.. ifds.Select (ifd => (0x8769, 4, 1L, (long)ifd))]);
		var noteAt = at;
		for (var k = 0; k < m; k++) {
			var entries = new List<(int, int, long, long)> ();
			foreach (var note in notesOfEachExifIfd[k]) {
				entries.Add ((0x927C, 7, note.Length, noteAt));
				tiff.Bytes (noteAt, note);
				noteAt += note.Length;
			}

			tiff.Table (ifds[k], [.. entries]);
		}

		return tiff.ToArray ();
	}

	static byte[] Kept (byte[] headroom, byte[] gain) => ExpectedHeadroomExif (headroom, gain)[10..];

	// Exif IFDs merge value by value: walking from the last, a value one lacks is taken from an earlier one.
	[TestMethod]
	public void Keep_TakesAValueTheLastExifIfdLacks_FromAnEarlierOne ()
	{
		var both = AppleMakerNote ([(0x0021, 10, 1, Real21), (0x0030, 10, 1, Real30)]);

		CollectionAssert.AreEqual (Kept (Alt21, Real30), AppleHeadroom.Keep (ExifWithNotes ([both], [AppleMakerNote ([(0x0021, 10, 1, Alt21)])])));
		CollectionAssert.AreEqual (Kept (Real21, Alt30), AppleHeadroom.Keep (ExifWithNotes ([both], [AppleMakerNote ([(0x0030, 10, 1, Alt30)])])));
	}

	// The last usable MakerNote of an Exif IFD wins: an Apple one of exactly its header and an empty table, or one
	// whose table ends exactly at its end (values may lie in the table), is usable; one whose count claims more
	// entries than it holds is not, and the one before it is used.
	[TestMethod]
	public void Keep_TheLastUsableMakerNote_AtTheEdgesOfUsable ()
	{
		var first = AppleMakerNote ([(0x0021, 10, 1, Real21), (0x0030, 10, 1, Real30)]);
		var empty = Concat (AppleMakerNoteHeader, [0, 0]);
		Assert.IsNull (AppleHeadroom.Keep (ExifWithNotes ([first, empty])), "an empty Apple maker note is the winner, and holds nothing");

		// Two entries whose values are the bytes of the table itself, at 16 and 24: exactly 40 bytes.
		var exact = Concat (AppleMakerNoteHeader, [0, 2], [0x00, 0x21, 0x00, 0x0A, 0, 0, 0, 1, 0, 0, 0, 16], [0x00, 0x30, 0x00, 0x0A, 0, 0, 0, 1, 0, 0, 0, 24]);
		CollectionAssert.AreEqual (Kept (exact[16..24], exact[24..32]), AppleHeadroom.Keep (ExifWithNotes ([first, exact])), "a table that ends at the note's end");

		var claims = exact.ToArray ();
		claims[15] = 3;
		CollectionAssert.AreEqual (Kept (Real21, Real30), AppleHeadroom.Keep (ExifWithNotes ([first, claims])), "a table past the note's end");
		CollectionAssert.AreEqual (Kept (Real21, Real30), AppleHeadroom.Keep (ExifWithNotes ([first, exact[..36]])), "a table 4 bytes past the note's end");
	}

	// A maker note holds only its counted entries: bytes after its table that read as a 0x0021 entry are not one.
	[TestMethod]
	public void Keep_ReadsOnlyTheMakerNotesCountedEntries ()
	{
		static byte[] Entry (int tag, int offset) => [(byte)(tag >> 8), (byte)tag, 0x00, 0x0A, 0, 0, 0, 1, 0, 0, 0, (byte)offset];
		// Two counted entries, then 12 bytes where the next-IFD offset would start that read as a third, then the values.
		var shadow = Concat (AppleMakerNoteHeader, [0, 2], Entry (0x0021, 52), Entry (0x0030, 60), Entry (0x0021, 68), Real21, Real30, Alt21);

		CollectionAssert.AreEqual (Kept (Real21, Real30), AppleHeadroom.Keep (ExifWithNotes ([shadow])));
	}
	// The walk reads at most one entry per 12 bytes of the TIFF, counting the Exif IFDs' and the maker notes'
	// entries. Here a second Exif IFD pointer names a table inside the maker note's entries whose count is a filler
	// entry's tag: with 6 the entries walked are exactly the bound and the headroom is kept; with 7 they pass it, and
	// nothing is.
	[TestMethod]
	[DataRow (6, true)]
	[DataRow (7, false)]
	public void Keep_AtTheEntryBound_KeepsTheHeadroom_AndPastItNothing (int fillerTag, bool kept)
	{
		const int Ifd0 = 8, Exif = Ifd0 + 2 + 24 + 4, Note = Exif + 2 + 12 + 4, Entries = 10;
		var valuesAt = 16 + (12 * Entries) + 4;
		var note = new List<byte> ();
		note.AddRange (AppleMakerNoteHeader);
		note.AddRange (new byte[] { 0, Entries });
		note.AddRange (new byte[] { 0x00, 0x21, 0x00, 0x0A, 0, 0, 0, 1, 0, 0, 0, (byte)valuesAt });
		note.AddRange (new byte[] { 0x00, 0x30, 0x00, 0x0A, 0, 0, 0, 1, 0, 0, 0, (byte)(valuesAt + 8) });
		for (var n = 2; n < Entries; n++)
			note.AddRange (new byte[] { 0x00, (byte)fillerTag, 0x00, 0x03, 0, 0, 0, 1, 0, 0, 0, 0 });
		note.AddRange (new byte[4]);
		note.AddRange (Real21);
		note.AddRange (Real30);
		var length = Note + note.Count;
		Assert.AreEqual (7 + Entries, length / 12, "fixture: the bound");
		var tiff = new TiffBuilder (length, bigEndian: true).Header (Ifd0)
			.Table (Ifd0, [(0x8769, 4, 1, Exif), (0x8769, 4, 1, Note + 16 + 24)])
			.Table (Exif, [(0x927C, 7, note.Count, Note)]).Bytes (Note, note.ToArray ()).ToArray ();

		var result = AppleHeadroom.Keep (tiff);

		if (kept)
			CollectionAssert.AreEqual (Kept (Real21, Real30), result);
		else
			Assert.IsNull (result);
	}
}
