# TagLibSharp2

A modern .NET library for reading and writing metadata in media files.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- **Modern .NET**: Built for .NET 8+ with nullable reference types, `Span<T>`, and async support
- **MIT License**: Permissive licensing for all use cases
- **Performance-First**: Zero-allocation parsing with `Span<T>` and `ArrayPool<T>`
- **Multi-Target**: Supports .NET Standard 2.0/2.1, .NET 8.0, and .NET 10.0
- **Format Support**:
  - Audio: MP3 (ID3v1/ID3v2), FLAC, OGG Vorbis
  - Planned: WAV, MP4, MKV, JPEG/PNG/TIFF (EXIF/XMP)

## Installation

```bash
dotnet add package TagLibSharp2
```

Or build from source:

```bash
git clone https://github.com/decriptor/tagsharp.git
cd tagsharp
dotnet build
```

## Quick Start

```csharp
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Xiph;
using TagLibSharp2.Ogg;

// Read ID3v2 tags from MP3 files
var mp3Data = File.ReadAllBytes("song.mp3");
var id3Result = Id3v2Tag.Read(mp3Data);
if (id3Result.IsSuccess)
{
    var tag = id3Result.Tag!;
    Console.WriteLine($"Title: {tag.Title}");
    Console.WriteLine($"Artist: {tag.Artist}");
    Console.WriteLine($"Album: {tag.Album}");
}

// Read FLAC metadata (sync)
var flacResult = FlacFile.ReadFromFile("song.flac");
if (flacResult.IsSuccess)
{
    var flac = flacResult.File!;
    Console.WriteLine($"Title: {flac.Title}");
    Console.WriteLine($"Artist: {flac.Artist}");
}

// Read FLAC metadata (async)
var flacAsync = await FlacFile.ReadFromFileAsync("song.flac");
if (flacAsync.IsSuccess)
{
    Console.WriteLine($"Title: {flacAsync.File!.Title}");
}

// Read Ogg Vorbis comments
var oggResult = OggVorbisFile.ReadFromFile("song.ogg");
if (oggResult.IsSuccess)
{
    var ogg = oggResult.File!;
    Console.WriteLine($"Title: {ogg.Title}");
}
```

See the [examples](examples/) directory for more comprehensive usage patterns.

## Building

```bash
git clone https://github.com/decriptor/tagsharp.git
cd tagsharp
dotnet build
dotnet test
```

## Project Status

This is a clean-room rewrite of media tagging functionality, designed from specifications rather than existing implementations.

### Phase 1: Core Infrastructure ✅
- [x] BinaryData (immutable binary data with Span<T> support)
- [x] BinaryDataBuilder (mutable builder with ArrayPool integration)
- [x] Multi-framework polyfills (netstandard2.0 through net10.0)
- [x] Tag and Picture abstract base classes
- [x] TagReadResult for error handling

### Phase 2: ID3 Support ✅
- [x] ID3v1/v1.1 reading and writing (id3.org specification)
- [x] ID3v2.3/2.4 reading and writing (id3.org specification)
  - [x] Text frames (TIT2, TPE1, TALB, TYER, TDRC, TCON, TRCK, TPE2, TPOS, TCOM, TBPM)
  - [x] Picture frames (APIC) with multiple picture types
  - [x] Syncsafe integer handling, multiple text encodings
  - [x] Extended header support

### Phase 3: Xiph Formats ✅
- [x] Vorbis Comments (xiph.org specification)
- [x] FLAC metadata blocks (xiph.org specification)
  - [x] StreamInfo, VorbisComment, Picture block support
- [x] Ogg container support with CRC validation

### Phase 4: I/O Abstraction ✅
- [x] File system abstraction for testability
- [x] Async file I/O support with cancellation
- [x] Extended metadata: Composer, BPM, AlbumArtist, DiscNumber

### Phase 5: File Writing & Media Properties ✅
- [x] FLAC file write operations with atomic saves
- [x] Media properties (duration, bitrate, sample rate, channels)
- [x] ID3v2 Comment (COMM) frame support

### Future
- [ ] Ogg Vorbis/ID3 file write operations
- [ ] Additional formats: WAV, MP4, MKV, EXIF

## Documentation

- [Architecture Overview](docs/ARCHITECTURE.md) - Design principles and allocation behavior
- [Core Types Reference](docs/CORE-TYPES.md) - Complete API documentation
- [Building Guide](docs/BUILDING.md) - Build instructions and requirements
- [Examples](examples/) - Working code samples

## Contributing

Contributions are welcome! Please read the [contributing guidelines](CONTRIBUTING.md) before submitting PRs.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
