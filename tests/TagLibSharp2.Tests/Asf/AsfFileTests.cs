// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Asf;

namespace TagLibSharp2.Tests.Asf;

[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Asf")]
public class AsfFileTests
{
	// ═══════════════════════════════════════════════════════════════
	// Format Detection Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Read_ValidAsfMagic_ReturnsSuccess ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess, result.Error);
	}

	[TestMethod]
	public void Read_InvalidMagic_ReturnsFailure ()
	{
		var data = new byte[100];
		data[0] = 0xFF; // Not ASF header GUID

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
		Assert.IsNotNull (result.Error);
	}

	[TestMethod]
	public void Read_EmptyInput_ReturnsFailure ()
	{
		var result = AsfFile.Read ([]);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_TruncatedHeader_ReturnsFailure ()
	{
		// Only 20 bytes - not enough for header (needs 30 min)
		var data = new byte[20];
		var headerGuid = AsfGuids.HeaderObject.Render ().ToArray ();
		Array.Copy (headerGuid, data, 16);

		var result = AsfFile.Read (data);

		Assert.IsFalse (result.IsSuccess);
	}

	// ═══════════════════════════════════════════════════════════════
	// Parsing Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_ExtractsTitle ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Test Song");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Song", result.Value.Tag.Title);
	}

	[TestMethod]
	public void Parse_ExtractsArtist ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Test Artist");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Test Artist", result.Value.Tag.Artist);
	}

	[TestMethod]
	public void Parse_ExtractsDuration ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (durationMs: 180000); // 3 minutes

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		// Duration should be approximately 180 seconds
		Assert.IsTrue (result.Value.AudioProperties.Duration.TotalSeconds >= 170);
		Assert.IsTrue (result.Value.AudioProperties.Duration.TotalSeconds <= 190);
	}

	[TestMethod]
	public void Parse_ExtractsBitrate ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (bitrate: 320000);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (320, result.Value.AudioProperties.Bitrate);
	}

	[TestMethod]
	public void Parse_ExtractsSampleRate ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (sampleRate: 48000);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (48000, result.Value.AudioProperties.SampleRate);
	}

	[TestMethod]
	public void Parse_ExtractsChannels ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (channels: 2);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.Value.AudioProperties.Channels);
	}

	[TestMethod]
	public void Parse_ExtractsBitsPerSample ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (bitsPerSample: 16);

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (16, result.Value.AudioProperties.BitsPerSample);
	}

	// ═══════════════════════════════════════════════════════════════
	// Tag Access Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Tag_Get_ReturnsAsfTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma ();

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.IsNotNull (result.Value.Tag);
		Assert.IsInstanceOfType<AsfTag> (result.Value.Tag);
	}

	[TestMethod]
	public void Title_Get_DelegatesToTag ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "Convenience Test");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Convenience Test", result.Value.Title);
	}

	// ═══════════════════════════════════════════════════════════════
	// Unicode Tests
	// ═══════════════════════════════════════════════════════════════

	[TestMethod]
	public void Parse_UnicodeTitle_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (title: "日本語タイトル");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("日本語タイトル", result.Value.Tag.Title);
	}

	[TestMethod]
	public void Parse_UnicodeArtist_Preserved ()
	{
		var data = AsfTestBuilder.CreateMinimalWma (artist: "Café Français");

		var result = AsfFile.Read (data);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual ("Café Français", result.Value.Tag.Artist);
	}
}
