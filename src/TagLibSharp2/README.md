# TagLibSharp2

A modern .NET library for reading and writing metadata in media files.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/v/TagLibSharp2.svg)](https://www.nuget.org/packages/TagLibSharp2/)

## Features

- **MIT License** - Permissive licensing for all use cases
- **Modern .NET** - Nullable types, `Span<T>`, and async I/O
- **Performance-First** - Zero-allocation parsing with `ArrayPool<T>`
- **Multi-Target** - .NET Standard 2.0/2.1, .NET 8.0, .NET 10.0

### Supported Formats

| Format | Read | Write | Audio Properties |
|--------|:----:|:-----:|:----------------:|
| MP3 (ID3v1/ID3v2) | ✅ | ✅ | ✅ |
| FLAC | ✅ | ✅ | ✅ |
| Ogg Vorbis | ✅ | ✅ | ✅ |
| WAV (RIFF INFO/ID3v2) | ✅ | ✅ | ✅ |
| AIFF/AIFC | ✅ | ✅ | ✅ |

## Quick Start

```csharp
using TagLibSharp2.Mpeg;
using TagLibSharp2.Xiph;

// Read MP3 tags
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;
    Console.WriteLine($"{mp3.Title} by {mp3.Artist}");
    Console.WriteLine($"Duration: {mp3.Duration}");

    // Modify tags
    mp3.Title = "New Title";
    mp3.Artist = "New Artist";
    mp3.SaveToFile("song.mp3", File.ReadAllBytes("song.mp3"));
}

// Read FLAC tags
var flacResult = FlacFile.ReadFromFile("song.flac");
if (flacResult.IsSuccess)
{
    var flac = flacResult.File!;
    Console.WriteLine($"{flac.Title} - {flac.Properties.Duration}");
}

// Async support
var asyncResult = await Mp3File.ReadFromFileAsync("song.mp3");
```

## Supported Metadata

- **Basic**: Title, Artist, Album, Year, Genre, Track, Comment
- **Extended**: AlbumArtist, Composer, DiscNumber, BPM, Conductor, Copyright
- **Pictures**: Album art with multiple picture types (front, back, etc.)
- **ReplayGain**: Track and album gain/peak values
- **MusicBrainz**: Track ID, Release ID, Artist ID, and more
- **Lyrics**: Multi-language synchronized and unsynchronized lyrics

## Error Handling

TagLibSharp2 uses result types instead of exceptions:

```csharp
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    // Use result.File
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

## Documentation

- [GitHub Repository](https://github.com/decriptor/TagLibSharp2)
- [Migration Guide](https://github.com/decriptor/TagLibSharp2/blob/main/docs/MIGRATION-FROM-TAGLIB.md)
- [API Documentation](https://github.com/decriptor/TagLibSharp2/blob/main/docs/CORE-TYPES.md)
- [Examples](https://github.com/decriptor/TagLibSharp2/tree/main/examples)

## License

MIT License - see [LICENSE](https://github.com/decriptor/TagLibSharp2/blob/main/LICENSE) for details.
