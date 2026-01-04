# TagLibSharp2 Cookbook

Practical recipes for common audio tagging tasks.

## Table of Contents

1. [Reading Tags](#reading-tags)
2. [Writing Tags](#writing-tags)
3. [Album Art](#album-art)
4. [MP4/M4A Files](#mp4m4a-files)
5. [Ogg Opus Files](#ogg-opus-files)
6. [DSF Files (DSD Audio)](#dsf-files-dsd-audio)
7. [APE Tags](#ape-tags)
8. [Batch Operations](#batch-operations)
9. [Format Conversion](#format-conversion)
10. [Advanced Patterns](#advanced-patterns)

---

## Reading Tags

### Read Any File (Auto-Detect Format)

```csharp
var result = MediaFile.Read("music.mp3");
if (result.IsSuccess)
{
    var tag = result.Tag!;
    Console.WriteLine($"Title: {tag.Title}");
    Console.WriteLine($"Artist: {tag.Artist}");
    Console.WriteLine($"Album: {tag.Album}");
    Console.WriteLine($"Track: {tag.Track}/{tag.TotalTracks}");
    Console.WriteLine($"Year: {tag.Year}");
    Console.WriteLine($"Format: {result.Format}");
}
```

### Read MP3 with Full Access

```csharp
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;

    // Access ID3v2 tag (preferred)
    if (mp3.Id3v2Tag is { } id3v2)
    {
        Console.WriteLine($"Title: {id3v2.Title}");
        Console.WriteLine($"BPM: {id3v2.BeatsPerMinute}");
        Console.WriteLine($"ISRC: {id3v2.Isrc}");

        // Get custom TXXX frames
        var customValue = id3v2.GetUserText("MY_CUSTOM_FIELD");
    }

    // Fallback to ID3v1 for legacy files
    if (mp3.Id3v1Tag is { } id3v1)
    {
        Console.WriteLine($"Title: {id3v1.Title}");
    }
}
```

### Read FLAC Metadata

```csharp
var result = FlacFile.ReadFromFile("album.flac");
if (result.IsSuccess)
{
    var flac = result.File!;
    var vorbis = flac.VorbisComment;

    Console.WriteLine($"Title: {vorbis?.Title}");
    Console.WriteLine($"ReplayGain: {vorbis?.ReplayGainTrackGain}");

    // Access stream info
    var streamInfo = flac.StreamInfo;
    Console.WriteLine($"Sample Rate: {streamInfo.SampleRate} Hz");
    Console.WriteLine($"Channels: {streamInfo.Channels}");
    Console.WriteLine($"Bits: {streamInfo.BitsPerSample}");
}
```

### Read Custom Fields

```csharp
// ID3v2 - User-defined text (TXXX frames)
var id3v2 = mp3.Id3v2Tag!;
var acoustId = id3v2.GetUserText("ACOUSTID_ID");
var customField = id3v2.GetUserText("MY_APP_DATA");

// Vorbis Comment - Any field
var vorbis = flac.VorbisComment!;
var acoustId = vorbis.GetValue("ACOUSTID_ID");
var customField = vorbis.GetValue("MY_APP_DATA");
```

---

## Writing Tags

### Basic Tag Writing

```csharp
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;

    mp3.Title = "New Title";
    mp3.Artist = "New Artist";
    mp3.Album = "New Album";
    mp3.Year = "2024";
    mp3.Track = 1;
    mp3.TotalTracks = 12;
    mp3.Genre = "Rock";

    var originalData = File.ReadAllBytes("song.mp3");
    mp3.SaveToFile("song.mp3", originalData);
}
```

### Write Extended Metadata

```csharp
var mp3 = result.File!;

// Disc information
mp3.DiscNumber = 1;
mp3.TotalDiscs = 2;

// Credits
mp3.Composer = "Composer Name";
mp3.Conductor = "Conductor Name";
mp3.AlbumArtist = "Various Artists";

// Production
mp3.Copyright = "2024 Record Label";
mp3.Publisher = "Record Label";
mp3.Isrc = "USRC12345678";
mp3.BeatsPerMinute = 120;

// Classification
mp3.Grouping = "Holiday";
mp3.Subtitle = "Radio Edit";
mp3.InitialKey = "Am";

// Compilation flag
mp3.IsCompilation = true;
```

### Write MusicBrainz IDs

```csharp
var tag = mp3.Id3v2Tag!;

tag.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
tag.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";
tag.MusicBrainzArtistId = "12345678-90ab-cdef-1234-567890abcdef";
tag.MusicBrainzReleaseGroupId = "abcdef12-3456-7890-abcd-ef1234567890";
tag.MusicBrainzAlbumArtistId = "fedcba98-7654-3210-fedc-ba9876543210";
tag.MusicBrainzReleaseStatus = "official";
tag.MusicBrainzReleaseType = "album";
```

### Write AcoustID Fingerprint

```csharp
var tag = mp3.Id3v2Tag!;

// After running fpcalc/chromaprint
tag.AcoustIdId = "f4e7c9d8-1234-5678-9abc-def012345678";
tag.AcoustIdFingerprint = "AQADtNQyRUkSRZEGAAAAAA...";
```

### Write ReplayGain and R128

```csharp
var tag = mp3.Id3v2Tag!;

// ReplayGain (decibel strings)
tag.ReplayGainTrackGain = "-6.50 dB";
tag.ReplayGainTrackPeak = "0.988547";
tag.ReplayGainAlbumGain = "-5.20 dB";
tag.ReplayGainAlbumPeak = "1.000000";

// R128 (Q7.8 format integers)
tag.R128TrackGain = "-512";  // -2 dB
tag.R128AlbumGain = "256";   // +1 dB
```

### Write Custom Fields

```csharp
// ID3v2 - User-defined text (TXXX frames)
var id3v2 = mp3.Id3v2Tag!;
id3v2.SetUserText("MY_APP_DATA", "custom value");
id3v2.SetUserText("CATALOG_ID", "ABC-12345");

// Vorbis Comment - Any field
var vorbis = flac.VorbisComment!;
vorbis.SetValue("MY_APP_DATA", "custom value");
vorbis.SetValue("CATALOG_ID", "ABC-12345");
```

---

## Album Art

### Read Album Art

```csharp
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess && result.File!.Pictures.Length > 0)
{
    foreach (var picture in result.File.Pictures)
    {
        Console.WriteLine($"Type: {picture.PictureType}");
        Console.WriteLine($"MIME: {picture.MimeType}");
        Console.WriteLine($"Size: {picture.PictureData.Length} bytes");

        // Save to file
        var extension = picture.MimeType switch {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".bin"
        };
        File.WriteAllBytes($"cover{extension}", picture.PictureData.ToArray());
    }
}
```

### Add Album Art to MP3

```csharp
using TagLibSharp2.Id3.Id3v2.Frames;

var mp3 = result.File!;
var imageBytes = File.ReadAllBytes("cover.jpg");

var picture = new PictureFrame(
    "image/jpeg",
    PictureType.FrontCover,
    "Album Cover",
    imageBytes);

mp3.Id3v2Tag?.AddPicture(picture);

// Add back cover
var backCover = new PictureFrame(
    "image/jpeg",
    PictureType.BackCover,
    "Back Cover",
    File.ReadAllBytes("back.jpg"));
mp3.Id3v2Tag?.AddPicture(backCover);
```

### Add Album Art to FLAC

```csharp
using TagLibSharp2.Xiph;

var flac = result.File!;
var imageBytes = File.ReadAllBytes("cover.jpg");

// Get image dimensions (simplified - use actual image library)
var picture = new FlacPicture(
    PictureType.FrontCover,
    "image/jpeg",
    "Album Cover",
    width: 500,
    height: 500,
    colorDepth: 24,
    colorCount: 0,
    new BinaryData(imageBytes));

flac.VorbisComment?.AddPicture(picture);
```

### Remove Album Art

```csharp
// Clear all pictures
mp3.Id3v2Tag!.Pictures = [];

// Or remove specific type
var pictures = mp3.Id3v2Tag!.Pictures.ToList();
pictures.RemoveAll(p => p.PictureType == PictureType.BackCover);
mp3.Id3v2Tag!.Pictures = pictures.ToArray();
```

---

## MP4/M4A Files

### Read MP4/M4A Metadata

```csharp
using TagLibSharp2.Mp4;

var result = Mp4File.ReadFromFile("song.m4a");
if (result.IsSuccess)
{
    var mp4 = result.File!;

    // Basic metadata
    Console.WriteLine($"Title: {mp4.Title}");
    Console.WriteLine($"Artist: {mp4.Artist}");
    Console.WriteLine($"Album: {mp4.Album}");
    Console.WriteLine($"Year: {mp4.Year}");
    Console.WriteLine($"Track: {mp4.Track}/{mp4.TotalTracks}");

    // Audio properties
    Console.WriteLine($"Duration: {mp4.Duration}");
    Console.WriteLine($"Codec: {mp4.AudioCodec}");  // Aac, Alac, or Unknown
    Console.WriteLine($"Sample Rate: {mp4.Properties?.SampleRate} Hz");
    Console.WriteLine($"Bitrate: {mp4.Properties?.Bitrate} kbps");
    Console.WriteLine($"Channels: {mp4.Properties?.Channels}");

    // Album art
    if (mp4.CoverArt is { } cover)
    {
        Console.WriteLine($"Cover: {cover.MimeType}, {cover.Data.Length} bytes");
        File.WriteAllBytes("cover.jpg", cover.Data.ToArray());
    }
}
```

### Write MP4/M4A Metadata

```csharp
using TagLibSharp2.Mp4;

var result = Mp4File.ReadFromFile("song.m4a");
if (result.IsSuccess)
{
    var mp4 = result.File!;

    // Basic tags
    mp4.Title = "My Song";
    mp4.Artist = "My Artist";
    mp4.Album = "My Album";
    mp4.Year = 2024;
    mp4.Track = 1;
    mp4.TotalTracks = 12;
    mp4.DiscNumber = 1;
    mp4.TotalDiscs = 2;
    mp4.Genre = "Rock";

    // Extended metadata
    mp4.Composer = "Composer Name";
    mp4.AlbumArtist = "Album Artist";
    mp4.Grouping = "Holiday";
    mp4.Description = "Song description";
    mp4.Copyright = "2024 Record Label";

    // Compilation flag
    mp4.IsCompilation = true;

    // MusicBrainz IDs
    mp4.MusicBrainzTrackId = "f4e7c9d8-1234-5678-9abc-def012345678";
    mp4.MusicBrainzReleaseId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

    // Save (uses SourcePath)
    mp4.SaveToFile();

    // Or save to different location
    mp4.SaveToFile("output.m4a");
}
```

### Add Album Art to MP4

```csharp
using TagLibSharp2.Mp4;

var result = Mp4File.ReadFromFile("song.m4a");
if (result.IsSuccess)
{
    var mp4 = result.File!;

    // Add cover art (auto-detects JPEG vs PNG)
    var imageBytes = File.ReadAllBytes("cover.jpg");
    mp4.SetCoverArt(new BinaryData(imageBytes));

    mp4.SaveToFile();
}
```

### Async MP4 Operations

```csharp
using TagLibSharp2.Mp4;

var result = await Mp4File.ReadFromFileAsync("song.m4a");
if (result.IsSuccess)
{
    var mp4 = result.File!;
    mp4.Title = "Updated Title";

    await mp4.SaveToFileAsync();
}
```

---

## Ogg Opus Files

### Read Opus with R128 Gain

```csharp
using TagLibSharp2.Ogg;

var result = OggOpusFile.ReadFromFile("song.opus");
if (result.IsSuccess)
{
    var opus = result.File!;

    // Basic metadata (via Vorbis Comment)
    Console.WriteLine($"Title: {opus.VorbisComment?.Title}");
    Console.WriteLine($"Artist: {opus.VorbisComment?.Artist}");

    // Audio properties
    var props = opus.Properties;
    Console.WriteLine($"Duration: {props?.Duration}");
    Console.WriteLine($"Sample Rate: {props?.SampleRate} Hz");
    Console.WriteLine($"Channels: {props?.Channels}");

    // R128 gain (per RFC 7845)
    Console.WriteLine($"Output Gain: {props?.OutputGain} dB");
    Console.WriteLine($"R128 Track Gain: {props?.R128TrackGain}");
    Console.WriteLine($"R128 Album Gain: {props?.R128AlbumGain}");

    // Multi-stream info
    Console.WriteLine($"Mapping Family: {props?.MappingFamily}");
    Console.WriteLine($"Stream Count: {props?.StreamCount}");
    Console.WriteLine($"Coupled Count: {props?.CoupledCount}");
}
```

### Write Opus Metadata

```csharp
using TagLibSharp2.Ogg;

var result = OggOpusFile.ReadFromFile("song.opus");
if (result.IsSuccess)
{
    var opus = result.File!;
    var vorbis = opus.VorbisComment ?? new VorbisComment();

    vorbis.Title = "My Song";
    vorbis.Artist = "My Artist";

    // Set R128 gain tags
    vorbis.SetValue("R128_TRACK_GAIN", "-512");  // -2 dB in Q7.8
    vorbis.SetValue("R128_ALBUM_GAIN", "256");   // +1 dB in Q7.8

    var data = File.ReadAllBytes("song.opus");
    opus.SaveToFile("song.opus", data);
}
```

---

## DSF Files (DSD Audio)

### Read DSF Metadata

```csharp
using TagLibSharp2.Dsf;

var result = DsfFile.ReadFromFile("song.dsf");
if (result.IsSuccess)
{
    var dsf = result.File!;

    // Metadata (via embedded ID3v2 tag)
    Console.WriteLine($"Title: {dsf.Id3v2Tag?.Title}");
    Console.WriteLine($"Artist: {dsf.Id3v2Tag?.Artist}");
    Console.WriteLine($"Album: {dsf.Id3v2Tag?.Album}");

    // DSD-specific audio properties
    var props = dsf.Properties;
    Console.WriteLine($"Sample Rate: {props?.SampleRate} Hz");
    Console.WriteLine($"DSD Format: {GetDsdFormat(props?.SampleRate)}");
    Console.WriteLine($"Channels: {props?.Channels}");
    Console.WriteLine($"Channel Type: {props?.ChannelType}");  // Mono, Stereo, Surround
    Console.WriteLine($"Duration: {props?.Duration}");
    Console.WriteLine($"Bits Per Sample: {props?.BitsPerSample}");
}

string GetDsdFormat(int? sampleRate) => sampleRate switch
{
    2822400 => "DSD64 (2.8 MHz)",
    5644800 => "DSD128 (5.6 MHz)",
    11289600 => "DSD256 (11.2 MHz)",
    22579200 => "DSD512 (22.5 MHz)",
    45158400 => "DSD1024 (45.1 MHz)",
    _ => "Unknown"
};
```

### Read Album Art from DSF

```csharp
using TagLibSharp2.Dsf;

var result = DsfFile.ReadFromFile("song.dsf");
if (result.IsSuccess)
{
    var dsf = result.File!;
    var pictures = dsf.Id3v2Tag?.Pictures;

    if (pictures?.Length > 0)
    {
        var cover = pictures[0];
        Console.WriteLine($"Cover: {cover.MimeType}, {cover.PictureData.Length} bytes");
        File.WriteAllBytes("cover.jpg", cover.PictureData.ToArray());
    }
}
```

### Write DSF Metadata

```csharp
using TagLibSharp2.Dsf;

var result = DsfFile.ReadFromFile("song.dsf");
if (result.IsSuccess)
{
    var dsf = result.File!;

    // Modify ID3v2 tag (creates one if not present)
    var tag = dsf.Id3v2Tag ?? new Id3v2Tag();
    tag.Title = "My DSD Song";
    tag.Artist = "My Artist";
    tag.Album = "My Album";

    // Save changes back to file
    dsf.SaveToFile();

    // Or save to a different location
    dsf.SaveToFile("output.dsf");
}
```

### Async DSF Operations

```csharp
using TagLibSharp2.Dsf;

// Async read
var result = await DsfFile.ReadFromFileAsync("song.dsf");
if (result.IsSuccess)
{
    var dsf = result.File!;
    Console.WriteLine($"Title: {dsf.Id3v2Tag?.Title}");
    Console.WriteLine($"Duration: {dsf.Properties?.Duration}");

    // Async write
    dsf.Id3v2Tag!.Title = "Updated Title";
    await dsf.SaveToFileAsync();
}
```

---

## APE Tags

APE (APE v2) tags are a flexible metadata format commonly used for ReplayGain data
and with lossless audio formats. TagLibSharp2 provides standalone APE tag support.

### Parse APE Tag from File Data

```csharp
using TagLibSharp2.Ape;

// APE tags are typically located at the end of audio files
var fileData = File.ReadAllBytes("song.ape");
var result = ApeTag.Parse(fileData);

if (result.IsSuccess)
{
    var ape = result.Tag!;

    Console.WriteLine($"Title: {ape.Title}");
    Console.WriteLine($"Artist: {ape.Artist}");
    Console.WriteLine($"Album: {ape.Album}");

    // ReplayGain (commonly stored in APE tags)
    Console.WriteLine($"Track Gain: {ape.ReplayGainTrackGain}");
    Console.WriteLine($"Track Peak: {ape.ReplayGainTrackPeak}");
    Console.WriteLine($"Album Gain: {ape.ReplayGainAlbumGain}");
    Console.WriteLine($"Album Peak: {ape.ReplayGainAlbumPeak}");
}
```

### Read Custom APE Fields

```csharp
using TagLibSharp2.Ape;

var fileData = File.ReadAllBytes("song.ape");
var result = ApeTag.Parse(fileData);

if (result.IsSuccess)
{
    var ape = result.Tag!;

    // APE tags support arbitrary field names (case-insensitive)
    var customValue = ape.GetValue("MY_CUSTOM_FIELD");
    var catalogId = ape.GetValue("CATALOG");

    // MusicBrainz IDs
    Console.WriteLine($"MB Track ID: {ape.MusicBrainzTrackId}");
    Console.WriteLine($"MB Release ID: {ape.MusicBrainzReleaseId}");
}
```

### Create and Write APE Tags

```csharp
using TagLibSharp2.Ape;

// Create a new APE tag
var ape = new ApeTag();

// Set standard fields
ape.Title = "My Song";
ape.Artist = "My Artist";
ape.Album = "My Album";
ape.Year = "2024";
ape.Track = 1;
ape.Genre = "Rock";

// Set ReplayGain values
ape.ReplayGainTrackGain = "-6.50 dB";
ape.ReplayGainTrackPeak = "0.988547";
ape.ReplayGainAlbumGain = "-5.20 dB";
ape.ReplayGainAlbumPeak = "0.995123";

// Set custom fields
ape.SetValue("CATALOG", "ABC-12345");
ape.SetValue("ENCODER", "My Encoder v1.0");

// Render to binary (with optional header)
var tagData = ape.RenderWithOptions(includeHeader: true);
```

### APE Tag with Binary Data (Cover Art)

```csharp
using TagLibSharp2.Ape;

var ape = new ApeTag();

// Add cover art as binary item
// APE spec requires filename prefix for binary items
var imageBytes = File.ReadAllBytes("cover.jpg");
ape.SetBinaryItem("Cover Art (Front)", "cover.jpg", imageBytes);

// Read binary item
var coverData = ape.GetBinaryItem("Cover Art (Front)");
if (coverData is not null)
{
    Console.WriteLine($"Cover filename: {coverData.Filename}");
    File.WriteAllBytes("extracted_cover.jpg", coverData.Data);
}
```

### APE Tag Properties

```csharp
using TagLibSharp2.Ape;

var ape = new ApeTag();

// Track and disc numbers support "N/M" format
ape.Track = 5;
ape.TotalTracks = 12;
ape.Disc = 1;
ape.TotalDiscs = 2;

// MusicBrainz identifiers
ape.MusicBrainzTrackId = "12345678-1234-1234-1234-123456789012";
ape.MusicBrainzReleaseId = "abcdefgh-abcd-abcd-abcd-abcdefghijkl";
ape.MusicBrainzArtistId = "11111111-2222-3333-4444-555555555555";

// Item count
Console.WriteLine($"Tag contains {ape.ItemCount} items");
```

---

## Batch Operations

### Process Multiple Files

```csharp
var files = Directory.GetFiles("music/", "*.mp3", SearchOption.AllDirectories);

var results = await BatchProcessor.ProcessAsync(
    files,
    async (path, ct) =>
    {
        var result = await Mp3File.ReadFromFileAsync(path, ct: ct);
        if (!result.IsSuccess) return null;

        return new {
            Path = path,
            Title = result.File!.Title,
            Artist = result.File.Artist
        };
    },
    maxDegreeOfParallelism: 4,
    progress: new Progress<BatchProgress>(p =>
        Console.WriteLine($"Processing: {p.PercentComplete:F1}%"))
);

// Analyze results
var successful = results.WhereSucceeded().ToList();
var failed = results.WhereFailed().ToList();

Console.WriteLine($"Processed: {successful.Count}, Failed: {failed.Count}");
```

### Bulk Update Tags

```csharp
var files = Directory.GetFiles("album/", "*.mp3");

var results = await BatchProcessor.ProcessAsync(
    files,
    async (path, ct) =>
    {
        var result = await Mp3File.ReadFromFileAsync(path, ct: ct);
        if (!result.IsSuccess) return false;

        var mp3 = result.File!;
        mp3.Album = "Corrected Album Name";
        mp3.AlbumArtist = "Various Artists";

        var data = await File.ReadAllBytesAsync(path, ct);
        await mp3.SaveToFileAsync(path, data, ct);
        return true;
    });

Console.WriteLine($"Updated {results.SuccessCount()} files");
```

### Find Files Missing Metadata

```csharp
var files = Directory.GetFiles("music/", "*.mp3", SearchOption.AllDirectories);

var missingData = await BatchProcessor.ProcessAsync(
    files,
    async (path, ct) =>
    {
        var result = await Mp3File.ReadFromFileAsync(path, ct: ct);
        if (!result.IsSuccess) return null;

        var mp3 = result.File!;
        var issues = new List<string>();

        if (string.IsNullOrEmpty(mp3.Title)) issues.Add("Missing title");
        if (string.IsNullOrEmpty(mp3.Artist)) issues.Add("Missing artist");
        if (mp3.Pictures.Length == 0) issues.Add("Missing album art");

        return issues.Count > 0 ? new { Path = path, Issues = issues } : null;
    });

foreach (var item in missingData.WhereSucceeded().Where(r => r.Value is not null))
{
    Console.WriteLine($"{item.Value!.Path}:");
    foreach (var issue in item.Value.Issues)
        Console.WriteLine($"  - {issue}");
}
```

---

## Format Conversion

### Copy Tags from MP3 to FLAC

```csharp
var mp3Result = Mp3File.ReadFromFile("song.mp3");
var flacResult = FlacFile.ReadFromFile("song.flac");

if (mp3Result.IsSuccess && flacResult.IsSuccess)
{
    var sourceTag = mp3Result.File!.Id3v2Tag;
    var targetTag = flacResult.File!.VorbisComment ?? new VorbisComment();

    if (sourceTag is not null)
    {
        // Copy all metadata
        sourceTag.CopyTo(targetTag);

        // Or copy selectively
        sourceTag.CopyTo(targetTag,
            TagCopyOptions.Basic |
            TagCopyOptions.Extended |
            TagCopyOptions.Pictures);
    }

    var data = File.ReadAllBytes("song.flac");
    flacResult.File.SaveToFile("song.flac", data);
}
```

### Copy Tags Preserving Existing Values

```csharp
// CopyTo only overwrites non-null source values
var sourceTag = new Id3v2Tag { Title = "New Title" };
var targetTag = new Id3v2Tag {
    Title = "Old Title",
    Artist = "Existing Artist"  // Will be preserved
};

sourceTag.CopyTo(targetTag);

Console.WriteLine(targetTag.Title);   // "New Title"
Console.WriteLine(targetTag.Artist);  // "Existing Artist"
```

---

## Advanced Patterns

### Validate Tags Before Saving

```csharp
var tag = mp3.Id3v2Tag!;
var validation = tag.Validate();

if (!validation.IsValid)
{
    Console.WriteLine("Validation issues found:");
    foreach (var issue in validation.AllIssues)
    {
        var icon = issue.Severity switch {
            ValidationSeverity.Error => "[ERROR]",
            ValidationSeverity.Warning => "[WARN]",
            _ => "[INFO]"
        };
        Console.WriteLine($"{icon} {issue.Field}: {issue.Message}");
    }

    if (validation.HasErrors)
    {
        Console.WriteLine("Cannot save - fix errors first");
        return;
    }
}

// Safe to save
mp3.SaveToFile(path, originalData);
```

### Work with Chapters (Podcasts/Audiobooks)

```csharp
using TagLibSharp2.Id3.Id3v2.Frames;

var tag = mp3.Id3v2Tag!;

// Read chapters
var chapters = tag.GetChapters();
foreach (var chapter in chapters)
{
    Console.WriteLine($"Chapter: {chapter.ElementId}");
    Console.WriteLine($"  Start: {chapter.StartTimeMs}ms");
    Console.WriteLine($"  End: {chapter.EndTimeMs}ms");
    if (chapter.Title is not null)
        Console.WriteLine($"  Title: {chapter.Title}");
}

// Add a chapter
var newChapter = new ChapterFrame(
    elementId: "chp01",
    startTimeMs: 0,
    endTimeMs: 300000);  // 5 minutes

// Add title to chapter
newChapter.AddSubFrame(new TextFrame("TIT2", "Introduction"));
tag.AddFrame(newChapter);
```

### Work with Synchronized Lyrics

```csharp
using TagLibSharp2.Id3.Id3v2.Frames;

var tag = mp3.Id3v2Tag!;

// Read synchronized lyrics
var syncLyrics = tag.GetSyncLyrics();
if (syncLyrics is not null)
{
    foreach (var item in syncLyrics.SyncItems)
    {
        Console.WriteLine($"[{item.Timestamp}ms] {item.Text}");
    }
}

// Add synchronized lyrics
var newLyrics = new SyncLyricsFrame
{
    Encoding = TextEncodingType.Utf8,
    Language = "eng",
    ContentType = SyncLyricsType.Lyrics,
    Description = "Lyrics"
};

newLyrics.AddSyncItem("First line", 0);
newLyrics.AddSyncItem("Second line", 3000);
tag.AddFrame(newLyrics);
```

### Access Popularimeter (Ratings/Play Counts)

```csharp
using TagLibSharp2.Id3.Id3v2.Frames;

var tag = mp3.Id3v2Tag!;

// Read ratings
var popm = tag.GetPOPM("my-email@example.com");
if (popm is not null)
{
    Console.WriteLine($"Rating: {popm.Rating}/255");
    Console.WriteLine($"Play Count: {popm.PlayCount}");
}

// Simple rating access (uses any email)
Console.WriteLine($"Rating: {tag.Rating}");

// Add/update rating
tag.Rating = 200;  // ~4 stars
```

### Safe Async Pattern

```csharp
async Task ProcessFileAsync(string path, CancellationToken ct)
{
    try
    {
        var result = await Mp3File.ReadFromFileAsync(path, ct: ct);
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Failed to read: {result.Error}");
            return;
        }

        var mp3 = result.File!;
        mp3.Title = "Updated";

        var data = await File.ReadAllBytesAsync(path, ct);
        await mp3.SaveToFileAsync(path, data, ct);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Operation cancelled");
    }
}

// Usage with timeout
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await ProcessFileAsync("song.mp3", cts.Token);
```

### Testable Code with IFileSystem

```csharp
// Production code uses default file system
public class TagService
{
    readonly IFileSystem _fileSystem;

    public TagService(IFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? DefaultFileSystem.Instance;
    }

    public async Task<string?> GetTitleAsync(string path)
    {
        var result = await Mp3File.ReadFromFileAsync(path, _fileSystem);
        return result.IsSuccess ? result.File!.Title : null;
    }
}

// Test code uses mock
[TestMethod]
public async Task GetTitle_ValidFile_ReturnsTitle()
{
    var mockFs = new MockFileSystem();
    mockFs.AddFile("test.mp3", CreateTestMp3("Test Title"));

    var service = new TagService(mockFs);

    var title = await service.GetTitleAsync("test.mp3");

    Assert.AreEqual("Test Title", title);
}
```
