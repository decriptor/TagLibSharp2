// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using TagLibSharp2.Mp4;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Chapter round trips on a real MP4 file.
/// </summary>
/// <remarks>
/// Skipped by default. Set TAGLIBSHARP2_TEST_MP4_CHAPTERS to an MP4/M4A/M4B/MOV file, for example one written by
/// <c>ffmpeg -i in.wav -i chapters.txt -map 0 -map_chapters 1 out.m4b</c>, then run
/// <c>dotnet test --filter "TestCategory=Integration"</c>.
/// </remarks>
[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Manual")]
[TestCategory ("Mp4")]
public class Mp4ChapterIntegrationTests : FileFormatTestBase
{
	const string TestFileEnvVar = "TAGLIBSHARP2_TEST_MP4_CHAPTERS";

	static (byte[] Data, Mp4File File) Open ()
	{
		var path = SkipIfNoTestFile (TestFileEnvVar, "MP4");
		var data = File.ReadAllBytes (path);
		var result = Mp4File.Read (data);
		Assert.IsTrue (result.IsSuccess, result.Error);
		return (data, result.File!);
	}

	[TestMethod]
	public void RealFile_ReadsChapters ()
	{
		var (_, file) = Open ();

		foreach (var chapter in file.Chapters)
			Console.WriteLine ($"{chapter.StartTime} - {chapter.EndTime} {chapter.Title}");
		Assert.IsTrue (file.Chapters.Count > 0, "expected a file with chapters");
		for (var i = 1; i < file.Chapters.Count; i++)
			Assert.IsTrue (file.Chapters[i].StartTime >= file.Chapters[i - 1].StartTime, "ordered");
	}

	[TestMethod]
	public void RealFile_ReplaceChapters_RoundTripsAndKeepsOtherTrackSamples ()
	{
		var (data, file) = Open ();
		var before = SampleHashes (data);
		Mp4Chapter[] chapters = [
			new (TimeSpan.Zero, "Opening"),
			new (TimeSpan.FromSeconds (1.25), "Middle ü 日本"),
			new (TimeSpan.FromSeconds (2.5), "End"),
		];

		file.Chapters = chapters;
		var saved = file.Render (data).ToArray ();

		var reread = Mp4File.Read (saved).File!;
		CollectionAssert.AreEqual (chapters, reread.Chapters.ToArray ());
		var after = SampleHashes (saved);
		var chapterTrack = Mp4ChapterCodec.FindChapterTrack (Mp4ChapterTestBuilder.Moov (saved))!.Id;
		foreach (var (id, hash) in before) {
			if (id == chapterTrack)
				continue;
			Assert.AreEqual (hash, after[id], $"track {id} samples unchanged");
		}
	}

	[TestMethod]
	public void RealFile_ClearChapters_RemovesBothForms ()
	{
		var (data, file) = Open ();

		file.Chapters = [];
		var saved = file.Render (data).ToArray ();

		var moov = Mp4ChapterTestBuilder.Moov (saved);
		Assert.IsNull (Mp4ChapterCodec.ReadChpl (moov));
		Assert.IsNull (Mp4ChapterCodec.FindChapterTrack (moov));
		Assert.AreEqual (0, Mp4File.Read (saved).File!.Chapters.Count);
	}

	[TestMethod]
	public void RealFile_TitleEdit_KeepsChapters ()
	{
		var (data, file) = Open ();
		var chapters = file.Chapters.ToArray ();

		file.Title = "Integration title";
		var saved = file.Render (data).ToArray ();

		CollectionAssert.AreEqual (chapters, Mp4File.Read (saved).File!.Chapters.ToArray ());
		CollectionAssert.AreEqual (SampleHashes (data), SampleHashes (saved));
	}

	/// <summary>
	/// SHA-256 of each track's samples inside the file, gathered through stsc, stsz, and stco/co64 (ISO 14496-12 §8.7).
	/// </summary>
	static Dictionary<uint, string> SampleHashes (byte[] file)
	{
		var hashes = new Dictionary<uint, string> ();
		foreach (var trak in Mp4ChapterTestBuilder.Moov (file).Children.Where (c => c.Type == "trak")) {
			var tkhd = trak.FindChild ("tkhd")!.Data.Span;
			var id = BinaryPrimitives.ReadUInt32BigEndian (tkhd.Slice (tkhd[0] == 1 ? 20 : 12));
			var stbl = trak.FindDescendant ("mdia", "minf", "stbl");
			if (stbl is null)
				continue;

			var stsz = stbl.FindChild ("stsz")!.Data.Span;
			var stsc = stbl.FindChild ("stsc")!.Data.Span;
			var co64 = stbl.FindChild ("co64");
			var stco = co64?.Data ?? stbl.FindChild ("stco")!.Data;
			var width = co64 is null ? 4 : 8;
			var constant = BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (4));
			var count = (int)BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (8));
			var chunks = (int)BinaryPrimitives.ReadUInt32BigEndian (stco.Span.Slice (4));
			var rows = (int)BinaryPrimitives.ReadUInt32BigEndian (stsc.Slice (4));

			using var sha = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
			var sample = 0;
			var row = 0;
			for (var chunk = 1; chunk <= chunks && sample < count; chunk++) {
				while (row + 1 < rows && BinaryPrimitives.ReadUInt32BigEndian (stsc.Slice (8 + ((row + 1) * 12))) <= chunk)
					row++;
				var perChunk = BinaryPrimitives.ReadUInt32BigEndian (stsc.Slice (8 + (row * 12) + 4));
				var at = width == 4
					? BinaryPrimitives.ReadUInt32BigEndian (stco.Span.Slice (8 + ((chunk - 1) * 4)))
					: (long)BinaryPrimitives.ReadUInt64BigEndian (stco.Span.Slice (8 + ((chunk - 1) * 8)));
				for (var k = 0; k < perChunk && sample < count; k++, sample++) {
					var size = constant != 0 ? constant : BinaryPrimitives.ReadUInt32BigEndian (stsz.Slice (12 + (sample * 4)));
					// A truncated file ends mid-track. Every offset moves by the same amount, so the same
					// samples stay inside the file before and after a save.
					if (at + size > file.Length)
						break;
					sha.AppendData (file, (int)at, (int)size);
					at += size;
				}
			}

			hashes[id] = Convert.ToHexString (sha.GetHashAndReset ());
		}

		return hashes;
	}
}
