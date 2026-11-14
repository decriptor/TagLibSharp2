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
}
