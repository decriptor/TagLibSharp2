// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Core;
using TagLibSharp2.Matroska;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Kit;

/// <summary>
/// One nesting structure a file can repeat without limit, wrapped in a minimal file of its format.
/// </summary>
/// <param name="Name">Short name used on the child-process command line.</param>
/// <param name="Format">The format <see cref="MediaFile"/> must detect.</param>
/// <param name="Extension">Extension for the path hint and the saved file.</param>
/// <param name="Structure">What repeats, for the test name and the report.</param>
/// <param name="Build">Builds the file at a depth.</param>
public sealed record DepthCase (string Name, MediaFormat Format, string Extension, string Structure, Func<int, byte[]> Build);

/// <summary>
/// Every structure in a supported format whose depth the file controls.
/// </summary>
/// <remarks>
/// This list is the depth row of the twin table: when a format gains a structure that nests (a new
/// container box, an element that may contain itself, embedded frames), add a case here. Each case
/// runs in <see cref="NestingDepthTests"/> at <see cref="Nesting.HostileDepth"/> levels.
/// </remarks>
public static class DepthCases
{
	/// <summary>A title long enough to make every format move or grow its tag.</summary>
	public static readonly string LongTitle = new ('N', 400);

	/// <summary>The cases, one per nesting structure.</summary>
	public static IReadOnlyList<DepthCase> All { get; } = [
		new ("matroska-simpletag", MediaFormat.Matroska, ".mkv",
			"Tags/Tag/SimpleTag nested in SimpleTag (RFC 9559 Tags)", MatroskaSimpleTags),
		new ("matroska-cues", MediaFormat.Matroska, ".mkv",
			"Cues/CuePoint nested in CuePoint, saved so clusters move", MatroskaCues),
		new ("matroska-chapters", MediaFormat.Matroska, ".mkv",
			"Chapters/EditionEntry/ChapterAtom nested in ChapterAtom (RFC 9559 Chapters)", MatroskaChapters),
		new ("mp4-udta", MediaFormat.Mp4, ".m4a",
			"moov/udta nested in udta, 64-bit sizes (ISO/IEC 14496-12 §4.2)", Mp4UserData),
		new ("mp4-track-layout", MediaFormat.Mp4, ".m4a",
			"moov/trak/mdia nested in mdia, 64-bit sizes", Mp4TrackLayout),
		new ("wav-list", MediaFormat.Wav, ".wav",
			"RIFF LIST nested in LIST INFO", WavLists),
		new ("mp3-id3v2-chap", MediaFormat.Mp3, ".mp3",
			"ID3v2.4 CHAP embedded in CHAP (ID3v2 Chapter Frame Addendum §3.1)", Id3Chapters),
		new ("mp3-id3v2-ctoc", MediaFormat.Mp3, ".mp3",
			"ID3v2.4 CTOC embedded in CTOC (ID3v2 Chapter Frame Addendum §3.2)", Id3Tocs),
	];

	/// <summary>Finds a case by name.</summary>
	public static DepthCase Find (string name) =>
		All.FirstOrDefault (c => c.Name == name)
		?? throw new ArgumentException ($"No depth case '{name}'. Known: {string.Join (", ", All.Select (c => c.Name))}", nameof (name));

	static byte[] MatroskaSimpleTags (int depth)
	{
		var leaf = Nesting.EbmlElement (MatroskaIds.SimpleTag, Nesting.Concat (
			Nesting.EbmlElement (MatroskaIds.TagName, "LEAF"u8),
			Nesting.EbmlElement (MatroskaIds.TagString, "deepest"u8)));
		var chain = Nesting.EbmlChain (MatroskaIds.SimpleTag, depth, Nesting.EbmlElement (MatroskaIds.TagName, "N"u8), leaf);
		var title = Nesting.EbmlElement (MatroskaIds.SimpleTag, Nesting.Concat (
			Nesting.EbmlElement (MatroskaIds.TagName, "TITLE"u8),
			Nesting.EbmlElement (MatroskaIds.TagString, "Outer"u8),
			chain));
		var targets = Nesting.EbmlElement (MatroskaIds.Targets, Nesting.EbmlElement (MatroskaIds.TargetTypeValue, [50]));
		var tags = Nesting.EbmlElement (MatroskaIds.Tags, Nesting.EbmlElement (MatroskaIds.Tag, Nesting.Concat (targets, title)));
		return MatroskaFile (Info (), tags);
	}

	static byte[] MatroskaCues (int depth)
	{
		// Each CueClusterPosition holds the Cluster's offset in the Segment body (RFC 9559, Cues: CueClusterPosition).
		// The new Tags go in before the Cluster, so a save must patch every one of them.
		var info = Info ();
		byte[] Cues (ulong position) => Nesting.EbmlElement (MatroskaIds.Cues, Nesting.EbmlChain (MatroskaIds.CuePoint, depth, [],
			Nesting.EbmlElement (MatroskaIds.CueTrackPositions, Nesting.Concat (
				Nesting.EbmlElement (0xF7, [1]),
				Nesting.EbmlElement (MatroskaIds.CueClusterPosition, BigEndian (position))))));
		var cues = Cues ((ulong)(info.Length + Cues (0).Length));
		var cluster = Nesting.EbmlUnknownSize (MatroskaIds.Cluster, Nesting.EbmlElement (0xE7, [0]));
		return MatroskaFile (info, cues, cluster);
	}

	static byte[] BigEndian (ulong value)
	{
		var bytes = new byte[8];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian (bytes, value);
		return bytes;
	}

	static byte[] MatroskaChapters (int depth)
	{
		// Each level is a ChapterAtom with its mandatory ChapterUID and ChapterTimeStart and a display
		// string, then the next ChapterAtom (RFC 9559, Chapters: ChapterAtom is a recursive element).
		var fields = Nesting.Concat (
			Nesting.EbmlElement (MatroskaIds.ChapterUid, [1]),
			Nesting.EbmlElement (MatroskaIds.ChapterTimeStart, [0]),
			Nesting.EbmlElement (MatroskaIds.ChapterDisplay, Nesting.EbmlElement (MatroskaIds.ChapString, "N"u8)));
		var leaf = Nesting.EbmlElement (MatroskaIds.ChapterAtom, Nesting.Concat (
			Nesting.EbmlElement (MatroskaIds.ChapterUid, [2]),
			Nesting.EbmlElement (MatroskaIds.ChapterTimeStart, [0]),
			Nesting.EbmlElement (MatroskaIds.ChapterDisplay, Nesting.EbmlElement (MatroskaIds.ChapString, "Deepest"u8))));
		var edition = Nesting.EbmlElement (MatroskaIds.EditionEntry, Nesting.Concat (
			Nesting.EbmlElement (MatroskaIds.EditionUid, [1]),
			Nesting.EbmlChain (MatroskaIds.ChapterAtom, depth, fields, leaf)));
		var tags = Nesting.EbmlElement (MatroskaIds.Tags, Nesting.EbmlElement (MatroskaIds.Tag, Nesting.Concat (
			Nesting.EbmlElement (MatroskaIds.Targets, Nesting.EbmlElement (MatroskaIds.TargetTypeValue, [50])),
			Nesting.EbmlElement (MatroskaIds.SimpleTag, Nesting.Concat (
				Nesting.EbmlElement (MatroskaIds.TagName, "TITLE"u8),
				Nesting.EbmlElement (MatroskaIds.TagString, "Outer"u8))))));
		return MatroskaFile (Info (), Nesting.EbmlElement (MatroskaIds.Chapters, edition), tags);
	}

	static byte[] Info () => Nesting.EbmlElement (MatroskaIds.Info, Nesting.EbmlElement (MatroskaIds.SegmentTitle, "Deep"u8));

	static byte[] MatroskaFile (params byte[][] segmentChildren)
	{
		var header = Nesting.EbmlElement (0x1A45DFA3, Nesting.EbmlElement (0x4282, "matroska"u8));
		return Nesting.Concat (header, Nesting.EbmlElement (MatroskaIds.Segment, Nesting.Concat (segmentChildren)));
	}

	static byte[] Mp4UserData (int depth) =>
		Mp4File (Nesting.IsoBoxChain ("udta", depth, Nesting.IsoBox ("free", [])));

	static byte[] Mp4TrackLayout (int depth) =>
		Mp4File (Nesting.IsoBox ("trak", Nesting.IsoBoxChain ("mdia", depth, Nesting.IsoBox ("free", []))));

	static byte[] Mp4File (byte[] chain)
	{
		// ftyp, then moov with an iTunes title (moov/udta/meta/ilst/©nam/data) so the file has a tag
		// to edit, then the chain (Apple QuickTime File Format, "Metadata"; ISO/IEC 14496-12 §8.11.1).
		var ftyp = TestBuilders.Mp4.CreateBox ("ftyp", "M4A \0\0\0\0M4A isom"u8.ToArray ());
		var data = TestBuilders.Mp4.CreateBox ("data", Nesting.Concat ([0, 0, 0, 1, 0, 0, 0, 0], "Outer"u8.ToArray ()));
		var ilst = TestBuilders.Mp4.CreateBox ("ilst", TestBuilders.Mp4.CreateBox ("\u00A9nam", data));
		var hdlr = TestBuilders.Mp4.CreateBox ("hdlr", Nesting.Concat (new byte[8], "mdir"u8.ToArray (), new byte[13]));
		var meta = TestBuilders.Mp4.CreateBox ("meta", Nesting.Concat (new byte[4], hdlr, ilst));
		var udta = TestBuilders.Mp4.CreateBox ("udta", meta);
		return Nesting.Concat (ftyp, Nesting.IsoBox ("moov", Nesting.Concat (udta, chain)));
	}

	static byte[] WavLists (int depth)
	{
		var name = Nesting.Concat ("INAM"u8.ToArray (), [6, 0, 0, 0], "Inner\0"u8.ToArray ());
		var chain = Nesting.RiffListChain ("INFO", depth, name);
		var wav = Nesting.Concat (TestBuilders.Wav.CreateMinimal (), chain);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian (wav.AsSpan (4), (uint)(wav.Length - 8));
		return wav;
	}

	static byte[] Id3Chapters (int depth) => Mp3File (Nesting.Id3v2ChapterChain (depth, "Deepest"));

	static byte[] Id3Tocs (int depth) => Mp3File (Nesting.Id3v2TocChain (depth, "Contents"));

	static byte[] Mp3File (byte[] frames)
	{
		// MPEG-1 Layer III, 128 kbps, 44.1 kHz: 417-byte frames (ISO/IEC 11172-3 §2.4.2.3).
		var frame = new byte[417];
		frame[0] = 0xFF;
		frame[1] = 0xFB;
		frame[2] = 0x90;
		return Nesting.Concat (Nesting.Id3v2Tag (frames), frame, frame);
	}
}

/// <summary>
/// What the child process does with one depth case: read through every entry point, edit, save, read again.
/// </summary>
public static class DepthProbe
{
	/// <summary>Exit code when the library threw instead of returning a failure result.</summary>
	public const int ThrewExitCode = 3;

	/// <summary>
	/// Runs the case and writes one line of step results.
	/// </summary>
	/// <remarks>
	/// A failure result is fine: refusing a structure past a bound is allowed. A throw is not, and
	/// neither is a save that succeeds but does not read back with the edit (<c>reread=fail</c>,
	/// <c>title-lost</c>). <see cref="Passed"/> applies that rule to the line.
	/// </remarks>
	/// <returns>0, or <see cref="ThrewExitCode"/> when a step threw.</returns>
	public static int Run (DepthCase depthCase, int depth, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull (depthCase);
		ArgumentNullException.ThrowIfNull (output);
		var step = "build";
		var line = $"{depthCase.Name} depth={depth}";
		try {
			var data = depthCase.Build (depth);
			line += $" bytes={data.Length}";
			var source = "/deep" + depthCase.Extension;
			var saved = "/saved" + depthCase.Extension;
			var files = new MockFileSystem ();
			files.AddFile (source, data);

			step = "span";
			line += $" span={Outcome (MediaFile.ReadFromData (data, source))}";

			step = "stream";
			using (var stream = new MemoryStream (data, writable: false))
				line += $" stream={Outcome (MediaFile.Read (stream, source))}";

			step = "async";
			using (var stream = new MemoryStream (data, writable: false))
				line += $" async={Outcome (MediaFile.ReadAsync (stream, source).GetAwaiter ().GetResult ())}";

			step = "path";
			var opened = MediaFile.Read (source, files);
			line += $" path={Outcome (opened)}";
			if (opened.IsSuccess) {
				step = "edit";
				if (opened.Tag is not null)
					opened.Tag.Title = DepthCases.LongTitle;
				line += $" edit={(opened.Tag is null ? "no-tag" : "ok")}";

				step = "save";
				var write = Save (opened.File!, data, saved, files);
				line += $" save={(write.IsSuccess ? "ok" : "fail(" + write.Error + ")")}";

				step = "reread";
				if (write.IsSuccess) {
					var again = MediaFile.Read (saved, files);
					line += $" reread={Outcome (again)}";
					if (again.IsSuccess && opened.Tag is not null && again.Tag?.Title != DepthCases.LongTitle)
						line += " title-lost";
				}
			}

			(opened.File as IDisposable)?.Dispose ();
			output.WriteLine (line);
			return 0;
		} catch (Exception ex) {
			output.WriteLine ($"{line} {step}=THREW {ex.GetType ().Name}: {ex.Message}");
			output.WriteLine (ex.StackTrace);
			return ThrewExitCode;
		}
	}

	delegate FileWriteResult SaveWithData (string path, ReadOnlySpan<byte> originalData, IFileSystem? fileSystem);

	/// <summary>
	/// Saves <paramref name="file"/> to <paramref name="path"/> through its own format type.
	/// </summary>
	/// <remarks>
	/// Uses <c>SaveToFile (string, ReadOnlySpan&lt;byte&gt;, IFileSystem)</c> with the bytes it was read from
	/// where the type has it, else <c>SaveToFile (string, IFileSystem)</c> (WAV and AIFF keep their bytes).
	/// The original-data overload does not depend on the file remembering where it was read from.
	/// </remarks>
	public static FileWriteResult Save (object file, ReadOnlySpan<byte> originalData, string path, IFileSystem files)
	{
		ArgumentNullException.ThrowIfNull (file);
		var type = file.GetType ();
		var withData = type.GetMethod ("SaveToFile", [typeof (string), typeof (ReadOnlySpan<byte>), typeof (IFileSystem)]);
		if (withData is not null)
			return withData.CreateDelegate<SaveWithData> (file) (path, originalData, files);

		var withPath = type.GetMethod ("SaveToFile", [typeof (string), typeof (IFileSystem)])
			?? throw new InvalidOperationException ($"{type.Name} has no SaveToFile overload the probe knows");
		try {
			return (FileWriteResult)withPath.Invoke (file, [path, files])!;
		} catch (TargetInvocationException ex) when (ex.InnerException is not null) {
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture (ex.InnerException).Throw ();
			throw;
		}
	}

	/// <summary>True when a probe line shows no throw, no unreadable save and no lost edit.</summary>
	public static bool Passed (string line) =>
		!line.Contains ("=THREW", StringComparison.Ordinal)
		&& !line.Contains ("reread=fail", StringComparison.Ordinal)
		&& !line.Contains ("title-lost", StringComparison.Ordinal);

	static string Outcome (MediaFileResult result) =>
		result.IsSuccess ? $"ok({result.Format})" : $"fail({result.Error})";
}
