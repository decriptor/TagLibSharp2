// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Dsf;

namespace TagLibSharp2.Tests.Dsf;

/// <summary>
/// Tests for high-resolution DSD audio format support.
/// Validates proper handling of DSD256, DSD512, and metadata round-trips.
/// </summary>
/// <remarks>
/// Added per audiophile review recommendation to validate edge cases
/// at extreme audio resolutions before v0.5.0 release.
/// </remarks>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("HighResolution")]
public class HighResolutionAudioTests
{
	[TestMethod]
	public void Dsf_DSD512_ParsesCorrectly ()
	{
		// Arrange - DSD512 (22.5792 MHz) - highest standard DSD rate
		var data = TestBuilders.Dsf.CreateMinimal (sampleRate: 22579200);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (22579200, result.File!.Properties!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD512, result.File.Properties.DsdRate);
	}

	[TestMethod]
	public void Dsf_DSD512_RoundTrip_PreservesMetadata ()
	{
		// Arrange - DSD512 with metadata
		var data = TestBuilders.Dsf.CreateWithMetadata (
			title: "DSD512 Test",
			artist: "High-Res Artist",
			sampleRate: 22579200);

		var result = DsfFile.Read (data);
		Assert.IsTrue (result.IsSuccess);

		var file = result.File!;
		file.EnsureId3v2Tag ().Album = "DSD512 Album";

		// Act - Round trip
		var rendered = file.Render ();
		var reparsed = DsfFile.Read (rendered.Span);

		// Assert
		Assert.IsTrue (reparsed.IsSuccess);
		Assert.AreEqual (22579200, reparsed.File!.Properties!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD512, reparsed.File.Properties.DsdRate);
		Assert.AreEqual ("DSD512 Test", reparsed.File.Id3v2Tag?.Title);
		Assert.AreEqual ("DSD512 Album", reparsed.File.Id3v2Tag?.Album);
	}

	[TestMethod]
	public void Dsf_DSD256_ParsesCorrectly ()
	{
		// Arrange - DSD256 (11.2896 MHz)
		var data = TestBuilders.Dsf.CreateMinimal (sampleRate: 11289600);

		// Act
		var result = DsfFile.Read (data);

		// Assert
		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (11289600, result.File!.Properties!.SampleRate);
		Assert.AreEqual (DsfSampleRate.DSD256, result.File.Properties.DsdRate);
	}
}
