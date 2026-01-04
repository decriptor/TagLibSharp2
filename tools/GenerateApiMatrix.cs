#pragma warning disable CA1305, IL2026, IL2070, IL2075
// API Matrix Generator for TagLibSharp2
// Run with: dotnet run --project tools/GenerateApiMatrix

using System.Reflection;
using System.Text;

// Find the assembly - try multiple locations
var searchPaths = new[] {
	Path.Combine (Environment.CurrentDirectory, "src", "TagLibSharp2", "bin", "Debug", "net10.0", "TagLibSharp2.dll"),
	Path.Combine (Environment.CurrentDirectory, "src", "TagLibSharp2", "bin", "Debug", "net8.0", "TagLibSharp2.dll"),
	Path.Combine (Environment.CurrentDirectory, "src", "TagLibSharp2", "bin", "Release", "net10.0", "TagLibSharp2.dll"),
};

var assemblyPath = args.Length > 0 ? args[0] : searchPaths.FirstOrDefault (File.Exists);

if (assemblyPath is null || !File.Exists (assemblyPath)) {
	Console.Error.WriteLine ("Assembly not found. Run 'dotnet build' first.");
	Console.Error.WriteLine ("Searched:");
	foreach (var p in searchPaths)
		Console.Error.WriteLine ($"  {p}");
	return 1;
}

Console.Error.WriteLine ($"Loading: {assemblyPath}");
var assembly = Assembly.LoadFrom (assemblyPath);

var tagType = assembly.GetType ("TagLibSharp2.Core.Tag")!;
var tagImpls = assembly.GetTypes ()
	.Where (t => t.IsClass && !t.IsAbstract && tagType.IsAssignableFrom (t))
	.OrderBy (t => t.Name switch {
		"Id3v1Tag" => 0, "Id3v2Tag" => 1, "VorbisComment" => 2, "Mp4Tag" => 3,
		"AsfTag" => 4, "ApeTag" => 5, "RiffInfoTag" => 6, _ => 99
	})
	.ToList ();

var baseProps = tagType.GetProperties (BindingFlags.Public | BindingFlags.Instance)
	.Where (p => p.DeclaringType == tagType && (p.GetGetMethod ()?.IsVirtual ?? false))
	.OrderBy (p => p.Name)
	.ToList ();

// Properties that work via base class delegation (array wrappers for singular properties)
var delegatedProps = new HashSet<string> {
	"Performers",      // delegates to Artist
	"AlbumArtists",    // delegates to AlbumArtist
	"Genres",          // delegates to Genre
	"Composers",       // delegates to Composer
	"PerformersSort",  // delegates to ArtistSort (array version)
	"AlbumArtistsSort", // delegates to AlbumArtistSort (array version)
	"ComposersSort",   // delegates to ComposerSort (array version)
	"PerformersRole",  // specialized array, format-specific override optional
};

// Properties that are aliases for other properties
var aliasProps = new Dictionary<string, string> {
	["MusicBrainzReleaseArtistId"] = "MusicBrainzAlbumArtistId",
};

// Obsolete properties that shouldn't count against coverage
var obsoleteProps = new HashSet<string> {
	"MusicIpId", // MusicIP service discontinued
};

// Computed properties (not stored, derived from other data)
var computedProps = new HashSet<string> {
	"IsEmpty",
};

// All excluded properties for coverage calculation
var excludedFromCoverage = new HashSet<string> (
	delegatedProps.Concat (aliasProps.Keys).Concat (obsoleteProps).Concat (computedProps)
);

Console.Error.WriteLine ($"Found {tagImpls.Count} tag types, {baseProps.Count} properties ({excludedFromCoverage.Count} excluded from coverage)");

// Build override matrix
var matrix = baseProps.ToDictionary (
	p => p.Name,
	p => tagImpls.ToDictionary (
		impl => impl.Name,
		impl => impl.GetProperty (p.Name)?.DeclaringType == impl
	)
);

// Short names and format limitations
var shortName = (string n) => n switch {
	"Id3v1Tag" => "ID3v1", "Id3v2Tag" => "ID3v2", "VorbisComment" => "Vorbis",
	"Mp4Tag" => "MP4", "AsfTag" => "ASF", "ApeTag" => "APE", "RiffInfoTag" => "RIFF", _ => n
};

var limited = new Dictionary<string, HashSet<string>> {
	["Id3v1Tag"] = [.. baseProps.Select (p => p.Name).Except (["Title", "Artist", "Album", "Year", "Comment", "Genre", "Track"])],
	["RiffInfoTag"] = [.. baseProps.Select (p => p.Name).Except (["Title", "Artist", "Album", "Year", "Comment", "Genre", "Track", "Copyright", "IsEmpty"])]
};

var categories = new (string Name, string[] Props)[] {
	("Core Metadata", ["Title", "Artist", "Album", "Year", "Comment", "Genre", "Track", "Pictures"]),
	("Extended Metadata", ["AlbumArtist", "Composer", "Conductor", "Copyright", "DiscNumber", "TotalDiscs", "TotalTracks",
		"BeatsPerMinute", "IsCompilation", "Subtitle", "Language", "OriginalReleaseDate", "Barcode", "CatalogNumber",
		"Publisher", "Lyrics", "Isrc", "Grouping", "Remixer", "InitialKey", "Mood", "MediaType",
		"EncodedBy", "EncoderSettings", "Description", "DateTagged", "AmazonId", "PodcastFeedUrl"]),
	("Classical Music", ["Work", "Movement", "MovementNumber", "MovementTotal"]),
	("Sort Order", ["TitleSort", "ArtistSort", "AlbumSort", "AlbumArtistSort", "ComposerSort"]),
	("ReplayGain / R128", ["ReplayGainTrackGain", "ReplayGainTrackPeak", "ReplayGainAlbumGain", "ReplayGainAlbumPeak", "R128TrackGain", "R128AlbumGain"]),
	("MusicBrainz IDs", ["MusicBrainzTrackId", "MusicBrainzReleaseId", "MusicBrainzArtistId", "MusicBrainzAlbumArtistId",
		"MusicBrainzReleaseGroupId", "MusicBrainzWorkId", "MusicBrainzDiscId", "MusicBrainzRecordingId",
		"MusicBrainzReleaseStatus", "MusicBrainzReleaseType", "MusicBrainzReleaseCountry", "AcoustIdId", "AcoustIdFingerprint"])
};

var sb = new StringBuilder ();
sb.AppendLine ("# TagLibSharp2 Format API Support Matrix");
sb.AppendLine ($"\n> Auto-generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n");

foreach (var (catName, props) in categories) {
	sb.AppendLine ($"### {catName}\n");
	sb.Append ("| Property |");
	foreach (var impl in tagImpls) sb.Append ($" {shortName (impl.Name)} |");
	sb.AppendLine ();

	sb.Append ("|----------|");
	foreach (var _ in tagImpls) sb.Append (":---:|");
	sb.AppendLine ();

	foreach (var prop in props.Where (matrix.ContainsKey)) {
		sb.Append ($"| {prop} |");
		foreach (var impl in tagImpls) {
			var isLimited = limited.GetValueOrDefault (impl.Name)?.Contains (prop) ?? false;
			var isOverridden = matrix[prop].GetValueOrDefault (impl.Name);
			sb.Append ($" {(isLimited ? "-" : isOverridden ? "✅" : "❌")} |");
		}
		sb.AppendLine ();
	}
	sb.AppendLine ();
}

// Summary - calculate coverage excluding delegated/obsolete/alias/computed properties
sb.AppendLine ("### Summary\n");
sb.AppendLine ("| Format | Implemented | Applicable | Coverage | Notes |");
sb.AppendLine ("|--------|:-----------:|:----------:|:--------:|-------|");

// Properties that count for coverage (excluding delegated, obsolete, alias, computed)
var coverageProps = baseProps.Where (p => !excludedFromCoverage.Contains (p.Name)).ToList ();

foreach (var impl in tagImpls) {
	var limitedProps = limited.GetValueOrDefault (impl.Name) ?? [];
	// Count implemented properties that are applicable to this format and not excluded
	var implemented = coverageProps.Count (p =>
		!limitedProps.Contains (p.Name) &&
		matrix[p.Name].GetValueOrDefault (impl.Name));
	var applicable = coverageProps.Count (p => !limitedProps.Contains (p.Name));
	var pct = applicable > 0 ? implemented * 100 / applicable : 100;
	var notes = impl.Name switch {
		"Id3v1Tag" => "Fixed 128-byte", "Id3v2Tag" => "Frame-based", "VorbisComment" => "Key=value",
		"Mp4Tag" => "Atom-based", "AsfTag" => "Descriptor-based", "ApeTag" => "Key-value", "RiffInfoTag" => "INFO chunk", _ => ""
	};
	sb.AppendLine ($"| **{shortName (impl.Name)}** | {implemented} | {applicable} | {pct}% | {notes} |");
}

sb.AppendLine ();
sb.AppendLine ($"> **Note:** {excludedFromCoverage.Count} properties excluded from coverage: delegated arrays ({delegatedProps.Count}), aliases ({aliasProps.Count}), obsolete ({obsoleteProps.Count}), computed ({computedProps.Count})");
sb.AppendLine ();

// ═══════════════════════════════════════════════════════════════
// File Class API Matrix
// ═══════════════════════════════════════════════════════════════

sb.AppendLine ("---\n");
sb.AppendLine ("## File Class APIs\n");

// Find all file classes (public classes ending in "File" with static Read/Parse methods)
var fileClasses = assembly.GetTypes ()
	.Where (t => t.IsClass && t.IsPublic && t.Name.EndsWith ("File", StringComparison.Ordinal) && !t.IsAbstract)
	.Where (t => t.GetMethods (BindingFlags.Public | BindingFlags.Static)
		.Any (m => m.Name is "Read" or "Parse" or "ReadFromFile" or "ReadFromFileAsync"))
	.OrderBy (t => t.Name)
	.ToList ();

Console.Error.WriteLine ($"Found {fileClasses.Count} file classes");

// Expected file API methods
var fileApiMethods = new[] {
	("Read", "Read(span)", true),           // static, parses binary data
	("Parse", "Parse(span)", true),         // static, alternative name for Read
	("ReadFromFile", "ReadFromFile(path)", true),       // static, reads from file
	("ReadFromFileAsync", "ReadFromFileAsync(path)", true), // static, async file read
	("Render", "Render()", false),          // instance, renders to binary
	("SaveToFile", "SaveToFile(path)", false),         // instance, saves to file
	("SaveToFileAsync", "SaveToFileAsync(path)", false),   // instance, async save
};

sb.AppendLine ("| File Class | Read | Parse | ReadFromFile | ReadFromFileAsync | Render | SaveToFile | SaveToFileAsync |");
sb.AppendLine ("|------------|:----:|:-----:|:------------:|:-----------------:|:------:|:----------:|:---------------:|");

var fileApiStats = new Dictionary<string, (int has, int total)> ();

foreach (var fileClass in fileClasses) {
	var methods = fileClass.GetMethods (BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

	sb.Append ($"| {fileClass.Name} |");

	var hasCount = 0;
	foreach (var (methodName, _, isStatic) in fileApiMethods) {
		var bindingFlags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
		var hasMethod = fileClass.GetMethods (bindingFlags).Any (m => m.Name == methodName);
		sb.Append ($" {(hasMethod ? "✅" : "-")} |");
		if (hasMethod) hasCount++;
	}
	sb.AppendLine ();

	fileApiStats[fileClass.Name] = (hasCount, fileApiMethods.Length);
}

sb.AppendLine ();

// File API consistency check
var hasRead = fileClasses.Where (t => t.GetMethods (BindingFlags.Public | BindingFlags.Static).Any (m => m.Name == "Read")).ToList ();
var hasParse = fileClasses.Where (t => t.GetMethods (BindingFlags.Public | BindingFlags.Static).Any (m => m.Name == "Parse")).ToList ();

if (hasRead.Count > 0 && hasParse.Count > 0) {
	sb.AppendLine ("> ⚠️ **Inconsistency:** Some files use `Read()`, others use `Parse()`. Consider standardizing on `Read()` to match TagLib#.");
	sb.AppendLine ($">   - `Read()`: {string.Join (", ", hasRead.Select (t => t.Name))}");
	sb.AppendLine ($">   - `Parse()`: {string.Join (", ", hasParse.Select (t => t.Name))}");
	sb.AppendLine ();
}

// ═══════════════════════════════════════════════════════════════
// Tag Class API Matrix
// ═══════════════════════════════════════════════════════════════

sb.AppendLine ("## Tag Class APIs\n");

// Expected tag API methods
var tagApiMethods = new[] {
	("Read", true),      // static, parses binary data
	("Parse", true),     // static, alternative name
	("Render", false),   // instance, renders to binary
	("Clear", false),    // instance, clears all data
};

sb.AppendLine ("| Tag Class | Read | Parse | Render | Clear |");
sb.AppendLine ("|-----------|:----:|:-----:|:------:|:-----:|");

foreach (var tagImpl in tagImpls) {
	sb.Append ($"| {tagImpl.Name} |");

	foreach (var (methodName, isStatic) in tagApiMethods) {
		var bindingFlags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
		var hasMethod = tagImpl.GetMethods (bindingFlags).Any (m => m.Name == methodName);
		sb.Append ($" {(hasMethod ? "✅" : "-")} |");
	}
	sb.AppendLine ();
}

sb.AppendLine ();
sb.AppendLine ("**Legend:** ✅ = Implemented, `-` = Not applicable/needed");

Console.WriteLine (sb);
return 0;
