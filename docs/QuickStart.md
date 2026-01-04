# QuickStart Guide

Get started with TagLibSharp2 in under 5 minutes.

## Installation

```bash
dotnet add package TagLibSharp2
```

## Reading Tags

### Any Supported Format (Auto-Detect)

```csharp
using TagLibSharp2.Core;

var result = MediaFile.Read("music.mp3");  // Also works with .flac, .ogg, .m4a, .opus
if (result.IsSuccess)
{
    Console.WriteLine($"Title:  {result.Tag?.Title}");
    Console.WriteLine($"Artist: {result.Tag?.Artist}");
    Console.WriteLine($"Album:  {result.Tag?.Album}");
    Console.WriteLine($"Year:   {result.Tag?.Year}");
    Console.WriteLine($"Format: {result.Format}");  // Mp3, Flac, OggVorbis, OggOpus, or Mp4
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

### Format-Specific Access

```csharp
using TagLibSharp2.Mpeg;
using TagLibSharp2.Xiph;
using TagLibSharp2.Ogg;

// MP3 files
var mp3 = Mp3File.ReadFromFile("song.mp3");
if (mp3.IsSuccess)
{
    var file = mp3.File!;
    Console.WriteLine($"Title: {file.Title}");

    // Access specific tag types
    var id3v2 = file.Id3v2Tag;  // ID3v2 tag (if present)
    var id3v1 = file.Id3v1Tag;  // ID3v1 tag (if present)
}

// FLAC files
var flac = FlacFile.ReadFromFile("song.flac");
if (flac.IsSuccess)
{
    var file = flac.File!;
    var vorbis = file.VorbisComment;  // Vorbis Comment tag
    Console.WriteLine($"Title: {vorbis?.Title}");
}

// Ogg Vorbis files
var ogg = OggVorbisFile.ReadFromFile("song.ogg");
if (ogg.IsSuccess)
{
    var file = ogg.File!;
    Console.WriteLine($"Title: {file.VorbisComment?.Title}");
}

// Ogg Opus files (RFC 7845)
var opus = OggOpusFile.ReadFromFile("song.opus");
if (opus.IsSuccess)
{
    var file = opus.File!;
    Console.WriteLine($"Title: {file.VorbisComment?.Title}");
    Console.WriteLine($"R128 Gain: {file.Properties?.OutputGain}dB");
}

// MP4/M4A files
using TagLibSharp2.Mp4;
var mp4 = Mp4File.ReadFromFile("song.m4a");
if (mp4.IsSuccess)
{
    var file = mp4.File!;
    Console.WriteLine($"Title: {file.Title}");
    Console.WriteLine($"Duration: {file.Duration}");
    Console.WriteLine($"Codec: {file.AudioCodec}");  // Aac, Alac, or Unknown
}

// DSF files (DSD high-resolution audio)
using TagLibSharp2.Dsf;
var dsf = DsfFile.ReadFromFile("song.dsf");
if (dsf.IsSuccess)
{
    var file = dsf.File!;
    Console.WriteLine($"Title: {file.Id3v2Tag?.Title}");
    Console.WriteLine($"Sample Rate: {file.Properties?.SampleRate}");  // DSD64=2822400, DSD128=5644800
    Console.WriteLine($"Channel Type: {file.Properties?.ChannelType}");  // Mono, Stereo, or Surround
}

// APE tagged files (MP3 with APEv2)
using TagLibSharp2.Ape;
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess && result.File!.ApeTag is { } ape)
{
    Console.WriteLine($"Title: {ape.Title}");
    Console.WriteLine($"ReplayGain: {ape.ReplayGainTrackGain}");
}
```

## Writing Tags

### MP3 Files

```csharp
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;

    // Modify tags
    mp3.Title = "My Song";
    mp3.Artist = "My Artist";
    mp3.Album = "My Album";
    mp3.Year = "2024";
    mp3.Track = 1;
    mp3.TotalTracks = 12;

    // Save changes
    var originalData = File.ReadAllBytes("song.mp3");
    mp3.SaveToFile("song.mp3", originalData);
}
```

### FLAC Files

```csharp
var result = FlacFile.ReadFromFile("song.flac");
if (result.IsSuccess)
{
    var flac = result.File!;
    var vorbis = flac.VorbisComment ?? new VorbisComment();

    vorbis.Title = "My Song";
    vorbis.Artist = "My Artist";

    var originalData = File.ReadAllBytes("song.flac");
    flac.SaveToFile("song.flac", originalData);
}
```

### MP4/M4A Files

```csharp
using TagLibSharp2.Mp4;

var result = Mp4File.ReadFromFile("song.m4a");
if (result.IsSuccess)
{
    var mp4 = result.File!;

    // Modify tags
    mp4.Title = "My Song";
    mp4.Artist = "My Artist";
    mp4.Album = "My Album";
    mp4.Year = 2024;
    mp4.Track = 1;
    mp4.TotalTracks = 12;

    // Save changes (reads original data from SourcePath)
    mp4.SaveToFile();
}
```

## Async Operations

All file operations support async for better performance:

```csharp
var result = await Mp3File.ReadFromFileAsync("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;
    mp3.Title = "Updated Title";

    var originalData = await File.ReadAllBytesAsync("song.mp3");
    await mp3.SaveToFileAsync("song.mp3", originalData);
}
```

## Batch Processing

Process multiple files in parallel:

```csharp
using TagLibSharp2.Core;

var files = Directory.GetFiles("music/", "*.mp3");

var results = await BatchProcessor.ProcessAsync(
    files,
    async (path, ct) =>
    {
        var result = await Mp3File.ReadFromFileAsync(path, ct: ct);
        return result.IsSuccess ? result.File!.Title : null;
    },
    maxDegreeOfParallelism: 4,
    progress: new Progress<BatchProgress>(p =>
        Console.WriteLine($"Progress: {p.PercentComplete:F1}%"))
);

// Filter results
var successful = results.WhereSucceeded().ToList();
var failed = results.WhereFailed().ToList();
Console.WriteLine($"Processed {successful.Count} files, {failed.Count} failed");
```

## Working with Pictures

### Reading Album Art

```csharp
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess && result.File!.Pictures.Length > 0)
{
    var picture = result.File.Pictures[0];
    Console.WriteLine($"Type: {picture.PictureType}");
    Console.WriteLine($"MIME: {picture.MimeType}");

    // Save to file
    File.WriteAllBytes("cover.jpg", picture.PictureData.ToArray());
}
```

### Adding Album Art

```csharp
using TagLibSharp2.Id3.Id3v2.Frames;

var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;
    var imageBytes = File.ReadAllBytes("cover.jpg");

    var picture = new PictureFrame(
        "image/jpeg",
        PictureType.FrontCover,
        "Album Cover",
        imageBytes);

    mp3.Id3v2Tag?.AddPicture(picture);
    mp3.SaveToFile("song.mp3", File.ReadAllBytes("song.mp3"));
}
```

## Tag Validation

Check tags for common issues:

```csharp
using TagLibSharp2.Core;

var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var tag = result.File!.Id3v2Tag;
    var validation = tag?.Validate();

    if (validation is not null)
    {
        foreach (var issue in validation.Issues)
        {
            Console.WriteLine($"[{issue.Severity}] {issue.Field}: {issue.Message}");
        }
    }
}
```

## Copying Tags Between Formats

```csharp
using TagLibSharp2.Core;

// Copy from MP3 to FLAC
var mp3Result = Mp3File.ReadFromFile("song.mp3");
var flacResult = FlacFile.ReadFromFile("song.flac");

if (mp3Result.IsSuccess && flacResult.IsSuccess)
{
    var sourceTag = mp3Result.File!.Id3v2Tag;
    var targetTag = flacResult.File!.VorbisComment;

    if (sourceTag is not null && targetTag is not null)
    {
        // Copy all metadata
        sourceTag.CopyTo(targetTag);

        // Or copy selectively
        sourceTag.CopyTo(targetTag, TagCopyOptions.Basic | TagCopyOptions.Pictures);

        flacResult.File.SaveToFile("song.flac", File.ReadAllBytes("song.flac"));
    }
}
```

## Error Handling

TagLibSharp2 uses result types instead of exceptions:

```csharp
var result = Mp3File.ReadFromFile("nonexistent.mp3");

if (!result.IsSuccess)
{
    // Handle error gracefully
    Console.WriteLine($"Failed to read file: {result.Error}");
    return;
}

// Safe to use result.File here
var mp3 = result.File!;
```

## Next Steps

- [API Reference](CORE-TYPES.md) - Detailed type documentation
- [Migration Guide](MIGRATION-FROM-TAGLIB.md) - For TagLib# users
- [Examples](../examples/) - More code samples
