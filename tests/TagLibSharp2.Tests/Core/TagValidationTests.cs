// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;

namespace TagLibSharp2.Tests.Core;

[TestClass]
[TestCategory ("Unit")]
public class TagValidationTests
{
	[TestMethod]
	public void ValidationResult_IsValidByDefault ()
	{
		var result = new ValidationResult ();

		Assert.IsTrue (result.IsValid);
		Assert.IsFalse (result.HasErrors);
		Assert.IsFalse (result.HasWarnings);
		Assert.AreEqual (0, result.ErrorCount);
		Assert.AreEqual (0, result.WarningCount);
	}

	[TestMethod]
	public void ValidationResult_AddError_SetsHasErrors ()
	{
		var result = new ValidationResult ();

		result.AddError ("Field", "Error message");

		Assert.IsFalse (result.IsValid);
		Assert.IsTrue (result.HasErrors);
		Assert.AreEqual (1, result.ErrorCount);
	}

	[TestMethod]
	public void ValidationResult_AddWarning_SetsHasWarnings ()
	{
		var result = new ValidationResult ();

		result.AddWarning ("Field", "Warning message");

		Assert.IsFalse (result.IsValid);
		Assert.IsTrue (result.HasWarnings);
		Assert.AreEqual (1, result.WarningCount);
	}

	[TestMethod]
	public void ValidationResult_AddInfo_DoesNotAffectValidity ()
	{
		var result = new ValidationResult ();

		result.AddInfo ("Field", "Info message");

		Assert.IsFalse (result.IsValid); // Any issue makes it invalid
		Assert.IsFalse (result.HasErrors);
		Assert.IsFalse (result.HasWarnings);
		Assert.HasCount (1, result.AllIssues);
	}

	[TestMethod]
	public void ValidationResult_GetIssues_FiltersBySeverity ()
	{
		var result = new ValidationResult ();
		result.AddError ("Field1", "Error");
		result.AddWarning ("Field2", "Warning");
		result.AddInfo ("Field3", "Info");

		var errors = result.GetIssues (ValidationSeverity.Error).ToList ();
		var warnings = result.GetIssues (ValidationSeverity.Warning).ToList ();

		Assert.HasCount (1, errors);
		Assert.HasCount (1, warnings);
	}

	[TestMethod]
	public void ValidationIssue_ToString_FormatsCorrectly ()
	{
		var issue = new ValidationIssue ("Track", ValidationSeverity.Warning, "Track exceeds total", "Fix it");

		var str = issue.ToString ();

		StringAssert.Contains (str, "[Warning]");
		StringAssert.Contains (str, "Track");
		StringAssert.Contains (str, "Track exceeds total");
		StringAssert.Contains (str, "Fix it");
	}

	[TestMethod]
	public void Validate_EmptyTag_ReturnsNoIssues ()
	{
		var tag = new Id3v2Tag ();

		var result = tag.Validate ();

		Assert.IsTrue (result.IsValid);
	}

	[TestMethod]
	public void Validate_TrackExceedsTotalTracks_ReturnsWarning ()
	{
		var tag = new Id3v2Tag {
			Track = 15,
			TotalTracks = 10
		};

		var result = tag.Validate ();

		Assert.IsTrue (result.HasWarnings);
		var warnings = result.GetIssues (ValidationSeverity.Warning).ToList ();
		Assert.HasCount (1, warnings);
		Assert.AreEqual ("Track", warnings[0].Field);
	}

	[TestMethod]
	public void Validate_DiscExceedsTotalDiscs_ReturnsWarning ()
	{
		var tag = new Id3v2Tag {
			DiscNumber = 3,
			TotalDiscs = 2
		};

		var result = tag.Validate ();

		Assert.IsTrue (result.HasWarnings);
		var warnings = result.GetIssues (ValidationSeverity.Warning).ToList ();
		Assert.HasCount (1, warnings);
		Assert.AreEqual ("DiscNumber", warnings[0].Field);
	}

	[TestMethod]
	public void Validate_InvalidYear_IsNotStored ()
	{
		// A year is four digits (TYER) or the year portion of a TDRC timestamp.
		// "500" is not a year, so the property does not keep it.
		var tag = new Id3v2Tag {
			Year = "500"
		};

		Assert.IsNull (tag.Year);
		Assert.IsFalse (tag.Validate ().GetIssues (ValidationSeverity.Warning).Any (i => i.Field == "Year"));
	}

	[TestMethod]
	public void Validate_ValidYear_ReturnsNoIssues ()
	{
		var tag = new Id3v2Tag {
			Year = "2024"
		};

		var result = tag.Validate ();

		Assert.IsFalse (result.HasWarnings);
	}

	[TestMethod]
	public void Validate_InvalidIsrc_ReturnsError ()
	{
		var tag = new Id3v2Tag ();
		tag.Isrc = "INVALID";

		var result = tag.Validate ();

		Assert.IsTrue (result.HasErrors);
		var errors = result.GetIssues (ValidationSeverity.Error).ToList ();
		Assert.HasCount (1, errors);
		Assert.AreEqual ("ISRC", errors[0].Field);
	}

	[TestMethod]
	public void Validate_ValidIsrc_ReturnsNoIssues ()
	{
		var tag = new Id3v2Tag ();
		tag.Isrc = "USRC17607839"; // Valid 12-char ISRC

		var result = tag.Validate ();

		Assert.IsFalse (result.HasErrors);
	}

	[TestMethod]
	public void Validate_ValidIsrcWithDashes_ReturnsNoIssues ()
	{
		var tag = new Id3v2Tag ();
		tag.Isrc = "US-RC1-76-07839"; // Valid ISRC with dashes

		var result = tag.Validate ();

		Assert.IsFalse (result.HasErrors);
	}

	[TestMethod]
	public void Validate_UnusualBpm_ReturnsWarning ()
	{
		var tag = new Id3v2Tag ();
		tag.BeatsPerMinute = 10; // Too low

		var result = tag.Validate ();

		Assert.IsTrue (result.HasWarnings);
		var warnings = result.GetIssues (ValidationSeverity.Warning).ToList ();
		Assert.HasCount (1, warnings);
		Assert.AreEqual ("BeatsPerMinute", warnings[0].Field);
	}

	[TestMethod]
	public void Validate_ValidBpm_ReturnsNoIssues ()
	{
		var tag = new Id3v2Tag ();
		tag.BeatsPerMinute = 120;

		var result = tag.Validate ();

		Assert.IsFalse (result.HasWarnings);
	}

	[TestMethod]
	public void Validate_ValidTag_ReturnsNoIssues ()
	{
		var tag = new Id3v2Tag {
			Title = "Test Song",
			Artist = "Test Artist",
			Album = "Test Album",
			Year = "2024",
			Track = 5,
			TotalTracks = 12,
			DiscNumber = 1,
			TotalDiscs = 2
		};
		tag.Isrc = "USRC17607839";
		tag.BeatsPerMinute = 128;

		var result = tag.Validate ();

		Assert.IsTrue (result.IsValid);
	}
}
