// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Globalization;
using TagLibSharp2.Ogg;

namespace TagLibSharp2.Tests.Ogg;

[TestClass]
[TestCategory ("Integration")]
[TestCategory ("Ogg")]
public class OggIntegrationTests
{
	const string MintCarPath = "/Users/sshaw/tmp/Mint_Car.ogg";
	const string JupiterCrashPath = "/Users/sshaw/tmp/Jupiter_Crash.ogg";

	[TestMethod]
	public void Read_MintCar_ParsesMetadata ()
	{
		if (!File.Exists (MintCarPath)) {
			Assert.Inconclusive ($"Test file not found: {MintCarPath}");
			return;
		}

		var fileData = File.ReadAllBytes (MintCarPath);
		var result = OggVorbisFile.Read (fileData);

		Console.WriteLine ($"Ogg Vorbis File Parsed: {result.IsSuccess}");

		if (!result.IsSuccess) {
			Console.WriteLine ($"  Error: {result.Error}");
			Assert.Fail ($"Failed to parse Ogg Vorbis file: {result.Error}");
			return;
		}

		var file = result.File!;
		Console.WriteLine ($"  Title: {file.Title ?? "(none)"}");
		Console.WriteLine ($"  Artist: {file.Artist ?? "(none)"}");
		Console.WriteLine ($"  Album: {file.Album ?? "(none)"}");
		Console.WriteLine ($"  Year: {file.Year ?? "(none)"}");
		Console.WriteLine ($"  Genre: {file.Genre ?? "(none)"}");
		Console.WriteLine ($"  Track: {file.Track?.ToString (CultureInfo.InvariantCulture) ?? "(none)"}");

		if (file.VorbisComment is not null) {
			Console.WriteLine ($"  Vendor String: {file.VorbisComment.VendorString}");
			Console.WriteLine ($"  Field Count: {file.VorbisComment.Fields.Count}");
			Console.WriteLine ("  All Fields:");
			foreach (var field in file.VorbisComment.Fields)
				Console.WriteLine ($"    {field.Name}={field.Value}");
		}

		// Verify we got some metadata
		Assert.IsNotNull (file.VorbisComment, "Expected Vorbis Comment to be parsed");
		Assert.IsFalse (string.IsNullOrEmpty (file.Title), "Expected Title to be present");
	}

	[TestMethod]
	public void Read_JupiterCrash_ParsesMetadata ()
	{
		if (!File.Exists (JupiterCrashPath)) {
			Assert.Inconclusive ($"Test file not found: {JupiterCrashPath}");
			return;
		}

		var fileData = File.ReadAllBytes (JupiterCrashPath);
		var result = OggVorbisFile.Read (fileData);

		Console.WriteLine ($"Ogg Vorbis File Parsed: {result.IsSuccess}");

		if (!result.IsSuccess) {
			Console.WriteLine ($"  Error: {result.Error}");
			Assert.Fail ($"Failed to parse Ogg Vorbis file: {result.Error}");
			return;
		}

		var file = result.File!;
		Console.WriteLine ($"  Title: {file.Title ?? "(none)"}");
		Console.WriteLine ($"  Artist: {file.Artist ?? "(none)"}");
		Console.WriteLine ($"  Album: {file.Album ?? "(none)"}");
		Console.WriteLine ($"  Year: {file.Year ?? "(none)"}");
		Console.WriteLine ($"  Genre: {file.Genre ?? "(none)"}");
		Console.WriteLine ($"  Track: {file.Track?.ToString (CultureInfo.InvariantCulture) ?? "(none)"}");

		if (file.VorbisComment is not null) {
			Console.WriteLine ($"  Vendor String: {file.VorbisComment.VendorString}");
			Console.WriteLine ($"  Field Count: {file.VorbisComment.Fields.Count}");
		}

		// Verify we got some metadata
		Assert.IsNotNull (file.VorbisComment, "Expected Vorbis Comment to be parsed");
	}

	[TestMethod]
	public void Read_MintCar_ValidatesCrcOnRequest ()
	{
		if (!File.Exists (MintCarPath)) {
			Assert.Inconclusive ($"Test file not found: {MintCarPath}");
			return;
		}

		var fileData = File.ReadAllBytes (MintCarPath);

		// Parse first Ogg page with CRC validation
		var result = OggPage.Read (fileData, validateCrc: true);

		Console.WriteLine ($"First Ogg Page CRC Valid: {result.IsSuccess}");

		if (!result.IsSuccess) {
			Console.WriteLine ($"  Error: {result.Error}");
		} else {
			Console.WriteLine ($"  Page Flags: {result.Page.Flags}");
			Console.WriteLine ($"  Serial Number: {result.Page.SerialNumber}");
			Console.WriteLine ($"  Sequence Number: {result.Page.SequenceNumber}");
			Console.WriteLine ($"  Data Length: {result.Page.Data.Length}");
		}

		Assert.IsTrue (result.IsSuccess, "Expected first page CRC to be valid");
	}

	[TestMethod]
	public void Read_OggFile_FirstPageIsBOS ()
	{
		if (!File.Exists (MintCarPath)) {
			Assert.Inconclusive ($"Test file not found: {MintCarPath}");
			return;
		}

		var fileData = File.ReadAllBytes (MintCarPath);
		var result = OggPage.Read (fileData);

		Assert.IsTrue (result.IsSuccess, "Expected to parse first page");
		Assert.IsTrue (result.Page.IsBeginOfStream, "First page should have BOS flag");
		Assert.IsFalse (result.Page.IsEndOfStream, "First page should not have EOS flag");
		Assert.IsFalse (result.Page.IsContinuation, "First page should not be a continuation");
	}

	[TestMethod]
	public void Read_OggFile_ContainsVorbisIdentificationHeader ()
	{
		if (!File.Exists (MintCarPath)) {
			Assert.Inconclusive ($"Test file not found: {MintCarPath}");
			return;
		}

		var fileData = File.ReadAllBytes (MintCarPath);
		var result = OggPage.Read (fileData);

		Assert.IsTrue (result.IsSuccess);

		// First page data should start with Vorbis identification header
		var data = result.Page.Data.Span;
		Assert.IsGreaterThanOrEqualTo (7, data.Length, "First page should have at least 7 bytes");

		// Packet type 1 + "vorbis"
		Assert.AreEqual ((byte)1, data[0], "Expected packet type 1 (identification)");
		Assert.AreEqual ((byte)'v', data[1]);
		Assert.AreEqual ((byte)'o', data[2]);
		Assert.AreEqual ((byte)'r', data[3]);
		Assert.AreEqual ((byte)'b', data[4]);
		Assert.AreEqual ((byte)'i', data[5]);
		Assert.AreEqual ((byte)'s', data[6]);

		Console.WriteLine ("Verified Vorbis identification header in first page");
	}
}
