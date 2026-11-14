// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Core;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class WavFileSavePreservationTests
{
	const int LoudnessOffset = 412;
	const int LoudnessSize = 10;

	[TestMethod]
	public void SaveToFile_DataChunkSizeSentinel_LeavesOriginalBytes ()
	{
		var original = CreateWaveWithDataChunkSize (0xFFFFFFFF);
		AssertSaveLeavesBytes (original, (file, fs) => file.SaveToFile ("/clip.wav", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_DataChunkSizeSentinel_LeavesOriginalBytes ()
	{
		var original = CreateWaveWithDataChunkSize (0xFFFFFFFF);
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", original);

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.InfoTag = new RiffInfoTag { Title = "Retitled" };

		var save = await read.File.SaveToFileAsync ("/clip.wav", fs);
		AssertMissingDataSaveFailed (original, fs.ReadAllBytes ("/clip.wav"), save);
	}

	[TestMethod]
	public async Task SaveToFileAsync_BackToSource_DataChunkSizeSentinel_LeavesOriginalBytes ()
	{
		var original = CreateWaveWithDataChunkSize (0xFFFFFFFF);
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", original);

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.InfoTag = new RiffInfoTag { Title = "Retitled" };

		var save = await read.File.SaveToFileAsync (fs);
		AssertMissingDataSaveFailed (original, fs.ReadAllBytes ("/clip.wav"), save);
	}

	[TestMethod]
	public void SaveToFile_ContainerSizeEndsBeforeData_LeavesTrailingBytes ()
	{
		var original = CreateWaveWhoseContainerEndsBeforeData ();
		AssertSaveLeavesBytes (original, (file, fs) => file.SaveToFile ("/clip.wav", fs));
	}

	[TestMethod]
	public async Task SaveToFileAsync_ContainerSizeEndsBeforeData_LeavesTrailingBytes ()
	{
		var original = CreateWaveWhoseContainerEndsBeforeData ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", original);

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);

		var save = await read.File!.SaveToFileAsync ("/clip.wav", fs);
		AssertMissingDataSaveFailed (original, fs.ReadAllBytes ("/clip.wav"), save);
	}

	[TestMethod]
	public void Render_DataChunkSizeSentinel_DoesNotReturnTruncatedWave ()
	{
		var read = WavFile.Read (CreateWaveWithDataChunkSize (0xFFFFFFFF));
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNull (read.File!.InfoTag);

		Assert.ThrowsExactly<InvalidOperationException> (() => read.File.Render ());
	}

	[TestMethod]
	public void SaveToFile_ShortBextAndOpaqueId3_SurviveTitleSave ()
	{
		var bext = "shortbex"u8.ToArray ();
		var id3 = "opaque-id3!!"u8.ToArray ();
		var original = CreateWaveWithChunks (
			("bext", bext),
			("id3 ", id3));

		var saved = SaveWithTitle (original);
		Assert.IsTrue (RiffFile.TryParse (new BinaryData (saved), out var riff));

		var savedBext = riff.GetChunk ("bext");
		var savedId3 = riff.GetChunk ("id3 ");
		Assert.IsTrue (savedBext.HasValue, "short bext was dropped");
		Assert.IsTrue (savedId3.HasValue, "opaque id3 payload was dropped");
		CollectionAssert.AreEqual (bext, savedBext.Value.Data.ToArray ());
		CollectionAssert.AreEqual (id3, savedId3.Value.Data.ToArray ());

		var roundTrip = WavFile.Read (saved).File;
		Assert.AreEqual ("Retitled", roundTrip?.InfoTag?.Title);
		Assert.IsNull (roundTrip?.BextTag);
		Assert.IsNull (roundTrip?.Id3v2Tag);
	}

	[TestMethod]
	public void SaveToFile_ParsedBextLoudness_SurvivesTitleSave ()
	{
		var bext = CreateBextWithLoudness ();
		var original = CreateWaveWithChunks (("bext", bext));

		var saved = SaveWithTitle (original);
		Assert.IsTrue (RiffFile.TryParse (new BinaryData (saved), out var riff));

		var savedBext = riff.GetChunk ("bext");
		Assert.IsTrue (savedBext.HasValue, "parsed bext was dropped");
		CollectionAssert.AreEqual (bext, savedBext.Value.Data.ToArray ());
		Assert.AreEqual ("Retitled", WavFile.Read (saved).File?.InfoTag?.Title);
	}

	[TestMethod]
	public void SaveToFile_ClearingParsedId3AndBext_RemovesThoseChunks ()
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", CreateWaveWithChunks (("bext", CreateBextWithLoudness ())));

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.InfoTag = new RiffInfoTag { Title = "InfoOld" };
		read.File.Id3v2Tag = new Id3v2Tag { Title = "Id3Old" };
		var written = read.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (written.IsSuccess, written.Error);

		var again = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (again.IsSuccess, again.Error);
		Assert.AreEqual ("Id3Old", again.File!.Title);
		Assert.IsNotNull (again.File.Id3v2Tag);
		Assert.IsNotNull (again.File.BextTag);

		again.File.InfoTag!.Title = "InfoNew";
		again.File.Id3v2Tag = null;
		again.File.BextTag = null;
		var cleared = again.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (cleared.IsSuccess, cleared.Error);

		var roundTrip = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (roundTrip.IsSuccess, roundTrip.Error);
		Assert.AreEqual ("InfoNew", roundTrip.File!.Title);
		Assert.IsNull (roundTrip.File.Id3v2Tag);
		Assert.IsNull (roundTrip.File.BextTag);

		Assert.IsTrue (RiffFile.TryParse (new BinaryData (fs.ReadAllBytes ("/clip.wav")), out var riff));
		Assert.IsFalse (riff.GetChunk ("id3 ").HasValue, "cleared ID3 chunk was written back");
		Assert.IsFalse (riff.GetChunk ("bext").HasValue, "cleared bext chunk was written back");
	}

	[TestMethod]
	public void Render_EditedBextDescription_PreservesLoudnessBytes ()
	{
		var bext = CreateBextWithLoudness ();
		var wav = WavFile.Read (CreateWaveWithChunks (("bext", bext))).File;
		Assert.IsNotNull (wav?.BextTag);

		wav.BextTag.Description = "Changed description";
		var rendered = wav.Render ();
		Assert.IsTrue (RiffFile.TryParse (rendered, out var riff));

		var savedBext = riff.GetChunk ("bext");
		Assert.IsTrue (savedBext.HasValue);
		CollectionAssert.AreEqual (
			bext.AsSpan (LoudnessOffset, LoudnessSize).ToArray (),
			savedBext.Value.Data.Slice (LoudnessOffset, LoudnessSize).ToArray ());

		var roundTrip = WavFile.Read (rendered).File;
		Assert.AreEqual ("Changed description", roundTrip?.BextTag?.Description);
	}

	[TestMethod]
	public void SaveToFile_BextFromAnotherFile_RendersAssignedDescription ()
	{
		var otherBytes = CreateBextWithDescription ("FromOtherFile");
		var other = BextTag.Parse (new BinaryData (otherBytes));
		Assert.IsNotNull (other);
		Assert.IsTrue (other.MatchesSource (), "assigned tag matches its own source and must still be rendered");

		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", CreateWaveWithChunks (("bext", CreateBextWithLoudness ())));

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.AreEqual ("Broadcast", read.File!.BextTag?.Description);
		read.File.BextTag = other;

		var save = read.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		var roundTrip = WavFile.Read (fs.ReadAllBytes ("/clip.wav")).File;
		Assert.AreEqual ("FromOtherFile", roundTrip?.BextTag?.Description);
	}

	[TestMethod]
	public void SaveToFile_BextLatin1DescriptionWithoutHistoryCrlf_CopiesBytes ()
	{
		var bext = CreateBextWithLatin1DescriptionAndBareHistory ();
		Assert.AreEqual (0xE9, bext[3]);
		Assert.AreNotEqual ((byte)'\n', bext[^1]);
		Assert.AreNotEqual ((byte)'\r', bext[^1]);

		var saved = SaveWithTitle (CreateWaveWithChunks (("bext", bext)));
		Assert.IsTrue (RiffFile.TryParse (new BinaryData (saved), out var riff));

		var savedBext = riff.GetChunk ("bext");
		Assert.IsTrue (savedBext.HasValue, "bext was dropped");
		CollectionAssert.AreEqual (bext, savedBext.Value.Data.ToArray ());
		Assert.AreEqual ("Retitled", WavFile.Read (saved).File?.InfoTag?.Title);
	}

	[TestMethod]
	public void Read_ChunkSizeNearIntMax_DoesNotThrow ()
	{
		AssertNearMaxChunkOmitted (sentinelContainer: false);
		AssertNearMaxChunkOmitted (sentinelContainer: true);
	}

	[TestMethod]
	public void SaveToFile_TruncatedDataChunk_WritesRenderedRiffSize ()
	{
		const uint declared = 0x00100000;
		var original = CreateWaveWithDataChunkSize (declared);
		var read = WavFile.Read (original);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNotNull (read.File);
		read.File.InfoTag = new RiffInfoTag { Title = "Retitled" };

		var rendered = read.File.Render ();
		Assert.IsTrue (rendered.Length < declared, $"render allocated or wrote the declared data size ({rendered.Length})");
		Assert.AreEqual ((uint)(rendered.Length - 8), rendered.ToUInt32LE (4));

		Assert.IsTrue (RiffFile.TryParse (rendered, out var riff));
		var data = riff.GetChunk ("data");
		Assert.IsTrue (data.HasValue);
		Assert.AreEqual (4u, data.Value.DataSize);
		CollectionAssert.AreEqual (new byte[] { 0x11, 0x22, 0x33, 0x44 }, data.Value.Data.ToArray ());
		Assert.AreEqual ("Retitled", WavFile.Read (rendered).File?.InfoTag?.Title);
	}

	[TestMethod]
	public void SaveToFile_TwoJunkChunks_BothSurvive ()
	{
		var first = new byte[] { 0x11, 0x22 };
		var second = new byte[] { 0x33, 0x44, 0x55 };
		var saved = SaveWithTitle (CreateWaveWithChunks (("JUNK", first), ("JUNK", second)));
		Assert.IsTrue (RiffFile.TryParse (new BinaryData (saved), out var riff));

		var junks = new List<RiffChunk> ();
		foreach (var chunk in riff.GetChunks ("JUNK"))
			junks.Add (chunk);

		Assert.AreEqual (2, junks.Count, "duplicate JUNK was collapsed");
		CollectionAssert.AreEqual (first, junks[0].Data.ToArray ());
		CollectionAssert.AreEqual (second, junks[1].Data.ToArray ());
		Assert.IsNotNull (riff.GetChunk ("data"), "the parsed data chunk must still count");
	}

	[TestMethod]
	public void SaveToFile_ClearedVersion0Umid_DoesNotRestoreSourceBytes ()
	{
		var bext = CreateBextWithUmidBytes (version: 0);
		var sourceVersion = bext[346] | (bext[347] << 8);
		Assert.IsTrue (sourceVersion < 1, "source version word at offset 346 must be < 1");
		var oldUmid = bext.AsSpan (348, 64).ToArray ();

		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", CreateWaveWithChunks (("bext", bext)));
		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNull (read.File!.BextTag?.Umid);

		read.File.BextTag!.Umid = null;
		read.File.InfoTag = new RiffInfoTag { Title = "Retitled" };
		var save = read.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		Assert.IsTrue (RiffFile.TryParse (new BinaryData (fs.ReadAllBytes ("/clip.wav")), out var riff));
		var saved = riff.GetChunk ("bext");
		Assert.IsTrue (saved.HasValue);
		CollectionAssert.AreNotEqual (oldUmid, saved.Value.Data.Slice (348, 64).ToArray ());
		CollectionAssert.AreEqual (new byte[64], saved.Value.Data.Slice (348, 64).ToArray ());
	}

	[TestMethod]
	public void SaveToFile_ClearedVersion1Umid_DoesNotWriteOldUmid ()
	{
		var bext = CreateBextWithUmidBytes (version: 1);
		var oldUmid = bext.AsSpan (348, 64).ToArray ();

		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", CreateWaveWithChunks (("bext", bext)));
		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNotNull (read.File!.BextTag?.Umid);

		read.File.BextTag.Umid = null;
		var save = read.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		Assert.IsTrue (RiffFile.TryParse (new BinaryData (fs.ReadAllBytes ("/clip.wav")), out var riff));
		var saved = riff.GetChunk ("bext");
		Assert.IsTrue (saved.HasValue);
		CollectionAssert.AreNotEqual (oldUmid, saved.Value.Data.Slice (348, 64).ToArray ());
		CollectionAssert.AreEqual (new byte[64], saved.Value.Data.Slice (348, 64).ToArray ());
	}

	[TestMethod]
	public void SaveToFile_SecondInfoList_IsKeptWhenOwnedInfoIsRewritten ()
	{
		var first = CreateInfoPayload ("First");
		var second = CreateInfoPayload ("SecondKeep");
		var adtl = CreateAdtlPayload ();
		var saved = SaveWithTitle (CreateWaveWithChunks (
			("LIST", first),
			("LIST", second),
			("LIST", adtl)));

		var content = new BinaryData (saved).ToStringLatin1 ();
		Assert.IsTrue (content.Contains ("Retitled"), "owned INFO list was not rewritten");
		Assert.IsTrue (content.Contains ("SecondKeep"), "second LIST INFO was dropped");
		Assert.IsFalse (content.Contains ("First"), "owned INFO list was copied and rewritten");
		Assert.IsTrue (content.Contains ("cue-labels"), "non-INFO LIST was dropped");
	}

	[TestMethod]
	public void SaveToFile_NewEmptyBextAndId3_ReplaceUnparsedChunks ()
	{
		var bext = "shortbex"u8.ToArray ();
		var id3 = "opaque-id3!!"u8.ToArray ();
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", CreateWaveWithChunks (("bext", bext), ("id3 ", id3)));

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNull (read.File!.BextTag);
		Assert.IsNull (read.File.Id3v2Tag);

		read.File.BextTag = new BextTag ();
		read.File.Id3v2Tag = new Id3v2Tag ();
		var save = read.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (save.IsSuccess, save.Error);

		Assert.IsTrue (RiffFile.TryParse (new BinaryData (fs.ReadAllBytes ("/clip.wav")), out var riff));
		Assert.IsFalse (riff.GetChunk ("bext").HasValue, "empty BextTag did not replace the unparsed chunk");
		Assert.IsFalse (riff.GetChunk ("id3 ").HasValue, "empty Id3v2Tag did not replace the unparsed chunk");
	}

	static void AssertNearMaxChunkOmitted (bool sentinelContainer)
	{
		var data = CreateWaveWithNearMaxChunk (sentinelContainer);
		var read = WavFile.Read (data);
		if (!read.IsSuccess) {
			Assert.IsNull (read.File);
			return;
		}

		Assert.IsTrue (RiffFile.TryParse (new BinaryData (data), out var riff));
		Assert.IsNotNull (riff.GetChunk ("fmt "));
		Assert.IsNotNull (riff.GetChunk ("data"));
		foreach (var chunk in riff.AllChunks)
			Assert.AreNotEqual (0x7FFFFFF0u, chunk.DataSize, sentinelContainer ? "sentinel container kept the oversized chunk" : "normal RIFF size kept the oversized chunk");
	}

	static void AssertSaveLeavesBytes (byte[] original, Func<WavFile, MockFileSystem, FileWriteResult> save)
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", original);

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		read.File!.InfoTag = new RiffInfoTag { Title = "Retitled" };

		var result = save (read.File, fs);
		AssertMissingDataSaveFailed (original, fs.ReadAllBytes ("/clip.wav"), result);
	}

	static void AssertMissingDataSaveFailed (byte[] original, byte[] written, FileWriteResult save)
	{
		Assert.IsFalse (
			save.IsSuccess,
			$"Save returned success and rewrote the file ({original.Length} -> {written.Length}).");
		CollectionAssert.AreEqual (original, written);
	}

	static byte[] SaveWithTitle (byte[] original)
	{
		var fs = new MockFileSystem ();
		fs.AddFile ("/clip.wav", original);

		var read = WavFile.ReadFromFile ("/clip.wav", fs);
		Assert.IsTrue (read.IsSuccess, read.Error);
		Assert.IsNotNull (read.File);
		read.File.InfoTag = new RiffInfoTag { Title = "Retitled" };

		var save = read.File.SaveToFile ("/clip.wav", fs);
		Assert.IsTrue (save.IsSuccess, save.Error);
		return fs.ReadAllBytes ("/clip.wav");
	}

	static byte[] CreateBextWithLoudness ()
	{
		var bext = new byte[BextTag.MinimumSize];
		var description = "Broadcast"u8;
		description.CopyTo (bext);
		bext[346] = 2;
		for (var i = 0; i < LoudnessSize; i++)
			bext[LoudnessOffset + i] = (byte)(0xA1 + i);
		bext[422] = 0x5A;
		return bext;
	}

	static byte[] CreateWaveWithDataChunkSize (uint dataSize)
	{
		using var body = new BinaryDataBuilder (64);
		body.AddStringLatin1 ("WAVE");
		AddFmt (body);
		body.AddStringLatin1 ("data");
		body.AddUInt32LE (dataSize);
		body.Add (0x11, 0x22, 0x33, 0x44);
		return WrapRiff (body.ToBinaryData (), (uint)body.Length);
	}

	static byte[] CreateWaveWhoseContainerEndsBeforeData ()
	{
		using var declared = new BinaryDataBuilder (48);
		declared.AddStringLatin1 ("WAVE");
		AddFmt (declared);

		using var file = new BinaryDataBuilder (64);
		file.AddStringLatin1 ("RIFF");
		file.AddUInt32LE ((uint)declared.Length);
		file.Add (declared.ToBinaryData ());
		file.AddStringLatin1 ("data");
		file.AddUInt32LE (4);
		file.Add (0x11, 0x22, 0x33, 0x44);
		return file.ToBinaryData ().ToArray ();
	}

	static byte[] CreateWaveWithChunks (params (string FourCc, byte[] Data)[] chunks)
	{
		using var body = new BinaryDataBuilder (1024);
		body.AddStringLatin1 ("WAVE");
		AddFmt (body);
		body.AddStringLatin1 ("data");
		body.AddUInt32LE (4);
		body.Add (0x11, 0x22, 0x33, 0x44);

		foreach (var (fourCc, data) in chunks) {
			body.AddStringLatin1 (fourCc);
			body.AddUInt32LE ((uint)data.Length);
			body.Add (data);
			if ((data.Length & 1) != 0)
				body.Add (0);
		}

		var bodyData = body.ToBinaryData ();
		return WrapRiff (bodyData, (uint)bodyData.Length);
	}

	static void AddFmt (BinaryDataBuilder body)
	{
		body.AddStringLatin1 ("fmt ");
		body.AddUInt32LE (16);
		body.AddUInt16LE (1);
		body.AddUInt16LE (1);
		body.AddUInt32LE (8000);
		body.AddUInt32LE (8000);
		body.AddUInt16LE (1);
		body.AddUInt16LE (8);
	}

	static byte[] WrapRiff (BinaryData body, uint declaredSize)
	{
		using var file = new BinaryDataBuilder (8 + body.Length);
		file.AddStringLatin1 ("RIFF");
		file.AddUInt32LE (declaredSize);
		file.Add (body);
		return file.ToBinaryData ().ToArray ();
	}

	static byte[] CreateBextWithDescription (string description)
	{
		var bext = new byte[BextTag.MinimumSize];
		var bytes = System.Text.Encoding.ASCII.GetBytes (description);
		bytes.AsSpan (0, Math.Min (bytes.Length, 256)).CopyTo (bext);
		bext[346] = 1;
		return bext;
	}

	static byte[] CreateBextWithLatin1DescriptionAndBareHistory ()
	{
		var history = "A=PCM,F=48000"u8;
		var bext = new byte[BextTag.MinimumSize + history.Length];
		bext[0] = (byte)'C';
		bext[1] = (byte)'a';
		bext[2] = (byte)'f';
		bext[3] = 0xE9;
		history.CopyTo (bext.AsSpan (BextTag.MinimumSize));
		return bext;
	}

	static byte[] CreateBextWithUmidBytes (byte version)
	{
		var bext = new byte[BextTag.MinimumSize];
		"Broadcast"u8.CopyTo (bext);
		bext[346] = version;
		for (var i = 0; i < 64; i++)
			bext[348 + i] = (byte)(0xA0 + (i % 16));
		return bext;
	}

	static byte[] CreateInfoPayload (string title)
	{
		var titleBytes = System.Text.Encoding.ASCII.GetBytes (title + "\0");
		using var builder = new BinaryDataBuilder (32);
		builder.AddStringLatin1 ("INFO");
		builder.AddStringLatin1 ("INAM");
		builder.AddUInt32LE ((uint)titleBytes.Length);
		builder.Add (titleBytes);
		if ((titleBytes.Length & 1) != 0)
			builder.Add (0);
		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] CreateAdtlPayload ()
	{
		var payload = "adtlcue-labels"u8;
		return payload.ToArray ();
	}

	static byte[] CreateWaveWithNearMaxChunk (bool sentinelContainer)
	{
		using var body = new BinaryDataBuilder (64);
		body.AddStringLatin1 ("WAVE");
		AddFmt (body);
		body.AddStringLatin1 ("data");
		body.AddUInt32LE (4);
		body.Add (0x11, 0x22, 0x33, 0x44);
		body.AddStringLatin1 ("JUNK");
		body.AddUInt32LE (0x7FFFFFF0);

		var bodyData = body.ToBinaryData ();
		var declared = sentinelContainer ? 0xFFFFFFFFu : (uint)bodyData.Length;
		return WrapRiff (bodyData, declared);
	}
}
