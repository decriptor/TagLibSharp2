# TagLibSharp2 Examples

This directory contains example projects demonstrating TagLibSharp2 usage.

## BasicUsage

Demonstrates core `BinaryData` and `BinaryDataBuilder` functionality:

- Creating BinaryData from various sources
- Reading integers (big-endian, little-endian, syncsafe)
- String encoding/decoding (Latin-1, UTF-8, UTF-16)
- Building binary structures with fluent API
- Pattern matching and searching
- CRC computation

### Running

```bash
cd examples/BasicUsage
dotnet run
```

## TagOperations

Demonstrates all tag reading and writing features:

- Creating ID3v2 tags from scratch
- Extended metadata (Conductor, Copyright, Compilation, Track/Disc totals)
- Lyrics (USLT frame) with multi-language support
- MusicBrainz IDs (TXXX and UFID frames)
- ReplayGain tags
- Comments (COMM frames)
- User-defined text (TXXX frames)
- Tag rendering and parsing round-trips
- Vorbis Comments for FLAC/Ogg
- ID3v1 tags

### Running

```bash
cd examples/TagOperations
dotnet run
```

## File Operations

For working with actual audio files, see the main README Quick Start section:

```csharp
// Read MP3 with unified ID3v1/ID3v2 access
var mp3 = Mp3File.ReadFromFile("song.mp3");
if (mp3.IsSuccess)
{
    Console.WriteLine($"Title: {mp3.File!.Title}");
    mp3.File.Title = "New Title";
    mp3.File.SaveToFile("song.mp3", File.ReadAllBytes("song.mp3"));
}

// Read FLAC
var flac = FlacFile.ReadFromFile("song.flac");
if (flac.IsSuccess)
{
    Console.WriteLine($"Title: {flac.File!.Title}");
}

// Read Ogg Vorbis
var ogg = OggVorbisFile.ReadFromFile("song.ogg");
if (ogg.IsSuccess)
{
    Console.WriteLine($"Title: {ogg.File!.Title}");
}
```
