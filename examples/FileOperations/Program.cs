// FileOperations Example
// Demonstrates file I/O patterns with TagLibSharp2

using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Xiph;
using TagLibSharp2.Ogg;

Console.WriteLine("TagLibSharp2 File Operations Examples");
Console.WriteLine(new string('=', 50));

// Example 1: Auto-detect and read any supported format
Console.WriteLine("\n1. Auto-Detect Format");
Console.WriteLine("-".PadRight(30, '-'));

await DemoAutoDetect ().ConfigureAwait (false);

// Example 2: Batch processing with progress
Console.WriteLine("\n2. Batch Processing");
Console.WriteLine("-".PadRight(30, '-'));

await DemoBatchProcessing ().ConfigureAwait (false);

// Example 3: Tag validation
Console.WriteLine("\n3. Tag Validation");
Console.WriteLine("-".PadRight(30, '-'));

DemoTagValidation ();

// Example 4: Cross-format tag copying
Console.WriteLine("\n4. Cross-Format Tag Copying");
Console.WriteLine("-".PadRight(30, '-'));

DemoTagCopy ();

// Example 5: Safe file operations
Console.WriteLine("\n5. Safe File Operations");
Console.WriteLine("-".PadRight(30, '-'));

await DemoSafeFileOps ().ConfigureAwait (false);

Console.WriteLine("\nAll examples completed!");

// --- Example Implementations ---

static async Task DemoAutoDetect ()
{
	// MediaFile.Open auto-detects the format based on magic bytes
	var formats = new[] { "song.mp3", "song.flac", "song.ogg" };

	foreach (var filename in formats) {
		// Create sample data for demo (in real use, these would be actual files)
		var sampleData = CreateSampleData (filename);
		var result = MediaFile.OpenFromData (sampleData, filename);

		if (result.IsSuccess) {
			Console.WriteLine ($"  {filename}: Format={result.Format}, Title={result.Tag?.Title ?? "(no title)"}");
		} else {
			Console.WriteLine ($"  {filename}: {result.Error}");
		}
	}

	// Async variant
	Console.WriteLine ("\n  Async file reading:");
	Console.WriteLine ("  await MediaFile.OpenAsync(path) - reads file asynchronously");
}

static async Task DemoBatchProcessing ()
{
	// Simulate processing multiple files
	var paths = Enumerable.Range (1, 10)
		.Select (i => $"track{i:D2}.mp3")
		.ToList ();

	Console.WriteLine ($"  Processing {paths.Count} files in parallel...\n");

	var results = await BatchProcessor.ProcessAsync (
		paths,
		async (path, ct) => {
			// Simulate file processing
			await Task.Delay (50, ct).ConfigureAwait (false);
			return $"Processed: {path}";
		},
		maxDegreeOfParallelism: 4,
		progress: new Progress<BatchProgress> (p => {
			// Progress callback
			var bar = new string ('#', (int)(p.PercentComplete / 10));
			Console.Write ($"\r  [{bar.PadRight (10)}] {p.PercentComplete:F0}% - {p.CurrentPath}");
		})
	).ConfigureAwait (false);

	Console.WriteLine ("\n");

	// Use extension methods to analyze results
	var successCount = results.SuccessCount ();
	var failureCount = results.FailureCount ();

	Console.WriteLine ($"  Results: {successCount} succeeded, {failureCount} failed");

	// Get specific results
	foreach (var success in results.WhereSucceeded ().Take (3)) {
		Console.WriteLine ($"    {success.Value}");
	}
}

static void DemoTagValidation ()
{
	// Create a tag with some issues
	var tag = new Id3v2Tag {
		Title = "  Untrimmed Title  ",  // Has whitespace
		Year = "not-a-year",             // Invalid year format
		Track = 15,
		TotalTracks = 10                 // Track > TotalTracks
	};
	tag.Isrc = "INVALID";                // Invalid ISRC

	var result = tag.Validate ();

	Console.WriteLine ($"  Validation found {result.AllIssues.Count} issues:\n");

	foreach (var issue in result.AllIssues) {
		var icon = issue.Severity switch {
			ValidationSeverity.Error => "[ERROR]",
			ValidationSeverity.Warning => "[WARN ]",
			_ => "[INFO ]"
		};
		Console.WriteLine ($"  {icon} {issue.Field}: {issue.Message}");
	}

	Console.WriteLine ($"\n  IsValid: {result.IsValid}");
	Console.WriteLine ($"  Error count: {result.ErrorCount}");
	Console.WriteLine ($"  Warning count: {result.WarningCount}");
}

static void DemoTagCopy ()
{
	// Create source tag with various metadata
	var source = new Id3v2Tag {
		Title = "Original Song",
		Artist = "Original Artist",
		Album = "Original Album",
		Year = "2024",
		Track = 5,
		TotalTracks = 12,
		Composer = "Composer Name",
		AlbumSort = "Album, The"
	};
	source.MusicBrainzTrackId = "abc123";

	// Copy to different tag types
	var id3Target = new Id3v2Tag ();
	var vorbisTarget = new VorbisComment ();

	// Full copy
	source.CopyTo (id3Target);
	Console.WriteLine ("  Full copy to ID3v2:");
	Console.WriteLine ($"    Title: {id3Target.Title}");
	Console.WriteLine ($"    Composer: {id3Target.Composer}");
	Console.WriteLine ($"    MusicBrainzTrackId: {id3Target.MusicBrainzTrackId}");

	// Selective copy - Basic only
	source.CopyTo (vorbisTarget, TagCopyOptions.Basic);
	Console.WriteLine ("\n  Basic-only copy to VorbisComment:");
	Console.WriteLine ($"    Title: {vorbisTarget.Title}");
	Console.WriteLine ($"    Composer: {vorbisTarget.Composer ?? "(not copied)"}");

	// Copy with multiple options
	var selective = new VorbisComment ();
	source.CopyTo (selective, TagCopyOptions.Basic | TagCopyOptions.MusicBrainz);
	Console.WriteLine ("\n  Basic + MusicBrainz copy:");
	Console.WriteLine ($"    Title: {selective.Title}");
	Console.WriteLine ($"    MusicBrainzTrackId: {selective.MusicBrainzTrackId}");
}

static async Task DemoSafeFileOps ()
{
	Console.WriteLine ("  Safe file operation patterns:\n");

	// Pattern 1: Always check result
	Console.WriteLine ("  1. Result-based error handling:");
	Console.WriteLine (@"     var result = Mp3File.ReadFromFile(path);
     if (!result.IsSuccess) {
         Console.WriteLine($""Error: {result.Error}"");
         return;
     }
     var mp3 = result.File!;");

	// Pattern 2: Atomic saves
	Console.WriteLine ("\n  2. Atomic file saves:");
	Console.WriteLine (@"     // Read original, modify, save atomically
     var originalData = await File.ReadAllBytesAsync(path);
     mp3.Title = ""New Title"";
     await mp3.SaveToFileAsync(path, originalData);");

	// Pattern 3: Use IFileSystem for testing
	Console.WriteLine ("\n  3. Testable file operations:");
	Console.WriteLine (@"     // Production code
     var result = Mp3File.ReadFromFile(path, DefaultFileSystem.Instance);

     // Test code
     var mockFs = new MockFileSystem();
     mockFs.AddFile(""test.mp3"", testData);
     var result = Mp3File.ReadFromFile(""test.mp3"", mockFs);");

	// Pattern 4: Cancellation support
	Console.WriteLine ("\n  4. Cancellation support:");
	Console.WriteLine (@"     using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
     var result = await Mp3File.ReadFromFileAsync(path, ct: cts.Token);");

	await Task.CompletedTask.ConfigureAwait (false);
}

static byte[] CreateSampleData (string filename)
{
	var ext = Path.GetExtension (filename).ToUpperInvariant ();

	return ext switch {
		".MP3" => CreateMinimalMp3 (),
		".FLAC" => CreateMinimalFlac (),
		".OGG" => CreateMinimalOgg (),
		_ => []
	};
}

static byte[] CreateMinimalMp3 ()
{
	// ID3v2.4 header with minimal TIT2 frame
	var header = new byte[] {
		0x49, 0x44, 0x33,       // "ID3"
		0x04, 0x00,             // Version 2.4.0
		0x00,                   // Flags
		0x00, 0x00, 0x00, 0x10  // Size (syncsafe): 16 bytes
	};

	// TIT2 frame with "Demo"
	var frame = new byte[] {
		0x54, 0x49, 0x54, 0x32, // "TIT2"
		0x00, 0x00, 0x00, 0x05, // Size: 5 bytes (syncsafe)
		0x00, 0x00,             // Flags
		0x03,                   // UTF-8 encoding
		0x44, 0x65, 0x6D, 0x6F  // "Demo"
	};

	return [.. header, .. frame];
}

static byte[] CreateMinimalFlac ()
{
	// fLaC magic + STREAMINFO
	var data = new byte[4 + 4 + 34];
	data[0] = 0x66; data[1] = 0x4C; data[2] = 0x61; data[3] = 0x43; // fLaC
	data[4] = 0x80; // STREAMINFO, last block
	data[5] = 0x00; data[6] = 0x00; data[7] = 0x22; // Size: 34
	return data;
}

static byte[] CreateMinimalOgg ()
{
	// Just enough to be recognized as OggS
	return [0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00];
}
