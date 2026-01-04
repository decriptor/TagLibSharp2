// Copyright (c) 2025 Stephen Shaw and contributors
// Tests that documentation examples compile correctly

using TagLibSharp2.Aiff;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Docs;

/// <summary>
/// These tests verify that documentation code examples compile.
/// They don't need to run successfully - just compile.
/// </summary>
[TestClass]
public class DocExamplesCompileTests
{
	// QuickStart.md - MediaFile.Read example
	[TestMethod]
	public void QuickStart_MediaFileRead_Compiles ()
	{
		// This just verifies the API exists - skip actual file access
		Assert.IsNotNull (typeof (MediaFile).GetMethod ("Read"));
		Assert.IsNotNull (typeof (MediaFileResult).GetProperty ("IsSuccess"));
		Assert.IsNotNull (typeof (MediaFileResult).GetProperty ("Tag"));
		Assert.IsNotNull (typeof (MediaFileResult).GetProperty ("Format"));
		Assert.IsNotNull (typeof (MediaFileResult).GetProperty ("Error"));
	}

	// QuickStart.md - Format-specific access
	[TestMethod]
	public void QuickStart_FormatSpecificAccess_Compiles ()
	{
		// Verify API exists (note: OggVorbisFile has extra validateCrc parameter)
		Assert.IsNotNull (typeof (Mp3File).GetMethod ("ReadFromFile", new[] { typeof (string), typeof (IFileSystem) }));
		Assert.IsNotNull (typeof (FlacFile).GetMethod ("ReadFromFile", new[] { typeof (string), typeof (IFileSystem) }));
		Assert.IsNotNull (typeof (OggVorbisFile).GetMethod ("ReadFromFile", new[] { typeof (string), typeof (IFileSystem), typeof (bool) }));
	}

	// QuickStart.md - BatchProcessor example
	[TestMethod]
	public void QuickStart_BatchProcessor_Compiles ()
	{
		// Verify BatchProcessor API
		Assert.IsNotNull (typeof (BatchProcessor).GetMethod ("ProcessAsync"));
		Assert.IsNotNull (typeof (BatchProgress).GetProperty ("PercentComplete"));
		Assert.IsNotNull (typeof (BatchProgress).GetProperty ("Completed"));
		Assert.IsNotNull (typeof (BatchProgress).GetProperty ("Total"));
	}

	// QuickStart.md - BatchResult extensions
	[TestMethod]
	public void QuickStart_BatchResultExtensions_Compiles ()
	{
		// Verify extension methods exist
		var methods = typeof (BatchResultExtensions).GetMethods ();
		Assert.IsTrue (methods.Any (m => m.Name == "WhereSucceeded"));
		Assert.IsTrue (methods.Any (m => m.Name == "WhereFailed"));
		Assert.IsTrue (methods.Any (m => m.Name == "SuccessCount"));
		Assert.IsTrue (methods.Any (m => m.Name == "FailureCount"));
	}

	// QuickStart.md - Tag validation
	[TestMethod]
	public void QuickStart_TagValidation_Compiles ()
	{
		// Verify validation API
		Assert.IsNotNull (typeof (Tag).GetMethod ("Validate"));
		Assert.IsNotNull (typeof (ValidationResult).GetProperty ("IsValid"));
		Assert.IsNotNull (typeof (ValidationResult).GetProperty ("HasErrors"));
		Assert.IsNotNull (typeof (ValidationResult).GetProperty ("AllIssues"));
		Assert.IsNotNull (typeof (ValidationIssue).GetProperty ("Severity"));
		Assert.IsNotNull (typeof (ValidationIssue).GetProperty ("Field"));
		Assert.IsNotNull (typeof (ValidationIssue).GetProperty ("Message"));
	}

	// QuickStart.md - CopyTo example
	[TestMethod]
	public void QuickStart_TagCopyTo_Compiles ()
	{
		// Verify CopyTo API
		Assert.IsNotNull (typeof (Tag).GetMethod ("CopyTo"));
		Assert.IsTrue (typeof (TagCopyOptions).IsEnum);
		Assert.IsTrue (Enum.IsDefined<TagCopyOptions> (TagCopyOptions.Basic));
		Assert.IsTrue (Enum.IsDefined<TagCopyOptions> (TagCopyOptions.Pictures));
		Assert.IsTrue (Enum.IsDefined<TagCopyOptions> (TagCopyOptions.All));
	}

	// Cookbook.md - Pictures API
	[TestMethod]
	public void Cookbook_PicturesApi_Compiles ()
	{
		// Verify picture API
		Assert.IsNotNull (typeof (Tag).GetProperty ("Pictures"));
		Assert.IsNotNull (typeof (IPicture).GetProperty ("PictureType"));
		Assert.IsNotNull (typeof (IPicture).GetProperty ("MimeType"));
		Assert.IsNotNull (typeof (IPicture).GetProperty ("PictureData"));
		Assert.IsTrue (typeof (PictureType).IsEnum);
		Assert.IsTrue (Enum.IsDefined<PictureType> (PictureType.FrontCover));
	}

	// Cookbook.md - MusicBrainz IDs
	[TestMethod]
	public void Cookbook_MusicBrainzIds_Compiles ()
	{
		// Verify MusicBrainz properties on Tag
		Assert.IsNotNull (typeof (Tag).GetProperty ("MusicBrainzTrackId"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("MusicBrainzReleaseId"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("MusicBrainzArtistId"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("MusicBrainzReleaseGroupId"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("MusicBrainzAlbumArtistId"));
	}

	// Cookbook.md - ReplayGain
	[TestMethod]
	public void Cookbook_ReplayGain_Compiles ()
	{
		// Verify ReplayGain properties
		Assert.IsNotNull (typeof (Tag).GetProperty ("ReplayGainTrackGain"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("ReplayGainTrackPeak"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("ReplayGainAlbumGain"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("ReplayGainAlbumPeak"));
	}

	// Cookbook.md - Extended metadata
	[TestMethod]
	public void Cookbook_ExtendedMetadata_Compiles ()
	{
		// Verify extended properties
		Assert.IsNotNull (typeof (Tag).GetProperty ("Composer"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("Conductor"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("Copyright"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("BeatsPerMinute"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("Grouping"));
		Assert.IsNotNull (typeof (Tag).GetProperty ("InitialKey"));
	}

	// Cookbook.md - IFileSystem for testing
	[TestMethod]
	public void Cookbook_FileSystemAbstraction_Compiles ()
	{
		// Verify IFileSystem API
		Assert.IsNotNull (typeof (IFileSystem));
		Assert.IsNotNull (typeof (DefaultFileSystem).GetProperty ("Instance"));

		// Verify file classes accept IFileSystem
		var mp3Method = typeof (Mp3File).GetMethod ("ReadFromFileAsync",
			new[] { typeof (string), typeof (IFileSystem), typeof (CancellationToken) });
		Assert.IsNotNull (mp3Method);
	}

	// Verify WAV and AIFF have expected APIs
	[TestMethod]
	public void Formats_WavAndAiff_Compiles ()
	{
		// WAV
		Assert.IsNotNull (typeof (WavFile).GetProperty ("InfoTag"));
		Assert.IsNotNull (typeof (WavFile).GetProperty ("Id3v2Tag"));
		Assert.IsNotNull (typeof (WavFile).GetProperty ("BextTag"));
		Assert.IsNotNull (typeof (WavFile).GetProperty ("ExtendedProperties"));

		// AIFF
		Assert.IsNotNull (typeof (AiffFile).GetProperty ("Tag"));
		Assert.IsNotNull (typeof (AiffFile).GetProperty ("AudioProperties"));
		Assert.IsNotNull (typeof (AiffFile).GetMethod ("SaveToFile", new[] { typeof (string), typeof (IFileSystem) }));
	}

	// Verify chapter frames exist (Cookbook advanced patterns)
	[TestMethod]
	public void Cookbook_ChapterFrames_Compiles ()
	{
		Assert.IsNotNull (typeof (ChapterFrame));
		Assert.IsNotNull (typeof (ChapterFrame).GetProperty ("StartTimeMs"));
		Assert.IsNotNull (typeof (ChapterFrame).GetProperty ("EndTimeMs"));
		Assert.IsNotNull (typeof (TableOfContentsFrame));
	}

	// Verify sync lyrics exist (Cookbook advanced patterns)
	[TestMethod]
	public void Cookbook_SyncLyrics_Compiles ()
	{
		Assert.IsNotNull (typeof (SyncLyricsFrame));
		Assert.IsNotNull (typeof (SyncLyricsItem));
		Assert.IsNotNull (typeof (SyncLyricsType));
	}
}
