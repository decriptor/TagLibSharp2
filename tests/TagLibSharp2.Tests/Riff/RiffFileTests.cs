// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Riff;

namespace TagLibSharp2.Tests.Riff;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Riff")]
public class RiffFileTests
{
	static BinaryData CreateMinimalWavRiff ()
	{
		return new BinaryData ([
			(byte)'R', (byte)'I', (byte)'F', (byte)'F',
			0x04, 0x00, 0x00, 0x00,
			(byte)'W', (byte)'A', (byte)'V', (byte)'E'
		]);
	}

	static BinaryData CreateRiffWithFmtChunk ()
	{
		using var builder = new BinaryDataBuilder (100);
		builder.AddStringLatin1 ("RIFF");
		builder.AddUInt32LE (20); // 4 + 8 + 8 (form + chunk header + 4 bytes data)
		builder.AddStringLatin1 ("WAVE");
		builder.AddStringLatin1 ("fmt ");
		builder.AddUInt32LE (4);
		builder.AddUInt32LE (0xDEADBEEF);
		return builder.ToBinaryData ();
	}

	[TestMethod]
	public void TryParse_ValidWaveFile_Succeeds ()
	{
		var data = CreateMinimalWavRiff ();
		var result = RiffFile.TryParse (data, out var file);

		Assert.IsTrue (result);
		Assert.IsTrue (file.IsValid);
		Assert.AreEqual ("WAVE", file.FormType);
	}

	[TestMethod]
	public void TryParse_TooShort_ReturnsFalse ()
	{
		var data = new BinaryData ([1, 2, 3, 4, 5, 6, 7, 8]);
		var result = RiffFile.TryParse (data, out _);
		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_WrongMagic_ReturnsFalse ()
	{
		var data = new BinaryData ([
			(byte)'X', (byte)'X', (byte)'X', (byte)'X',
			0x04, 0x00, 0x00, 0x00,
			(byte)'W', (byte)'A', (byte)'V', (byte)'E'
		]);
		var result = RiffFile.TryParse (data, out _);
		Assert.IsFalse (result);
	}

	[TestMethod]
	public void TryParse_WithChunks_ParsesChunk ()
	{
		var data = CreateRiffWithFmtChunk ();
		Assert.IsTrue (RiffFile.TryParse (data, out var file));

		var chunk = file.GetChunk ("fmt ");
		Assert.IsNotNull (chunk);
		Assert.AreEqual ("fmt ", chunk.Value.FourCC);
	}

	[TestMethod]
	public void GetChunk_NonExistent_ReturnsNull ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateMinimalWavRiff (), out var file));
		Assert.IsNull (file.GetChunk ("fmt "));
	}

	[TestMethod]
	public void SetChunk_AddsNewChunk ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateMinimalWavRiff (), out var file));
		file.SetChunk (new RiffChunk ("test", new BinaryData ([1, 2, 3])));

		Assert.AreEqual (1, file.AllChunks.Count);
		Assert.AreEqual ("test", file.AllChunks[0].FourCC);
	}

	[TestMethod]
	public void SetChunk_ReplacesExisting ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateRiffWithFmtChunk (), out var file));
		file.SetChunk (new RiffChunk ("fmt ", new BinaryData ([0xAA, 0xBB])));

		Assert.AreEqual (1, file.AllChunks.Count);
		Assert.AreEqual (2u, file.AllChunks[0].DataSize);
	}

	[TestMethod]
	public void RemoveChunks_ExistingChunk_ReturnsTrue ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateRiffWithFmtChunk (), out var file));
		Assert.IsTrue (file.RemoveChunks ("fmt "));
		Assert.AreEqual (0, file.AllChunks.Count);
	}

	[TestMethod]
	public void RemoveChunks_NonExistent_ReturnsFalse ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateMinimalWavRiff (), out var file));
		Assert.IsFalse (file.RemoveChunks ("fmt "));
	}

	[TestMethod]
	public void Render_ProducesValidRiff ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateMinimalWavRiff (), out var file));
		file.SetChunk (new RiffChunk ("test", new BinaryData ([1, 2, 3, 4])));

		var rendered = file.Render ();

		Assert.AreEqual ((byte)'R', rendered[0]);
		Assert.AreEqual ((byte)'W', rendered[8]);
	}

	[TestMethod]
	public void Render_RoundTrip_PreservesData ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateRiffWithFmtChunk (), out var original));

		var rendered = original.Render ();
		Assert.IsTrue (RiffFile.TryParse (rendered, out var roundTripped));

		Assert.AreEqual (original.FormType, roundTripped.FormType);
		Assert.AreEqual (original.AllChunks.Count, roundTripped.AllChunks.Count);
	}

	[TestMethod]
	public void FileSize_MatchesHeaderValue ()
	{
		Assert.IsTrue (RiffFile.TryParse (CreateMinimalWavRiff (), out var file));
		Assert.AreEqual (4u, file.FileSize);
	}

	[TestMethod]
	public void TryParse_FileSizeSentinel_ParsesFmtAndDataChunks ()
	{
		var data = CreateWaveWithDeclaredSize (0xFFFFFFFF);
		Assert.IsTrue (RiffFile.TryParse (data, out var file));

		Assert.AreEqual (0xFFFFFFFFu, file.FileSize);
		Assert.AreEqual (2, file.AllChunks.Count);

		var fmt = file.GetChunk ("fmt ");
		Assert.IsNotNull (fmt);
		Assert.AreEqual (16u, fmt.Value.DataSize);

		var dataChunk = file.GetChunk ("data");
		Assert.IsNotNull (dataChunk);
		Assert.AreEqual (4u, dataChunk.Value.DataSize);
		Assert.IsTrue (dataChunk.Value.Data.Equals (new BinaryData ([0x11, 0x22, 0x33, 0x44])));
	}

	[TestMethod]
	public void TryParse_FileSizeHighBit_ParsesFmtAndDataChunks ()
	{
		var data = CreateWaveWithDeclaredSize (0x80000000);
		Assert.IsTrue (RiffFile.TryParse (data, out var file));

		Assert.AreEqual (2, file.AllChunks.Count);
		Assert.IsNotNull (file.GetChunk ("fmt "));
		Assert.IsNotNull (file.GetChunk ("data"));
	}

	static BinaryData CreateWaveWithDeclaredSize (uint declaredSize)
	{
		using var body = new BinaryDataBuilder (64);
		body.AddStringLatin1 ("WAVE");

		body.AddStringLatin1 ("fmt ");
		body.AddUInt32LE (16);
		body.AddUInt16LE (1);
		body.AddUInt16LE (1);
		body.AddUInt32LE (8000);
		body.AddUInt32LE (8000);
		body.AddUInt16LE (1);
		body.AddUInt16LE (8);

		body.AddStringLatin1 ("data");
		body.AddUInt32LE (4);
		body.Add (0x11, 0x22, 0x33, 0x44);

		var bodyData = body.ToBinaryData ();
		using var file = new BinaryDataBuilder (8 + bodyData.Length);
		file.AddStringLatin1 ("RIFF");
		file.AddUInt32LE (declaredSize);
		file.Add (bodyData);
		return file.ToBinaryData ();
	}
}
