# TagLibSharp2

A modern .NET library for reading and writing metadata in media files.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## When to Use TagLibSharp2

**Choose TagLibSharp2 if you need:**
- MIT license (TagLib# is LGPL)
- Async I/O for high-throughput scenarios
- Modern .NET features (nullable types, `Span<T>`)
- Result-based error handling (no exceptions)

**Choose TagLib# if you need:**
- MP4/M4A, ASF/WMA, or APE support (not yet implemented here)
- A battle-tested library used in production for years

See the [Migration Guide](docs/MIGRATION-FROM-TAGLIB.md) for detailed comparison.

## Features

- **Modern .NET**: Built for .NET 8+ with nullable reference types, `Span<T>`, and async support
- **MIT License**: Permissive licensing for all use cases
- **Performance-First**: Zero-allocation parsing with `Span<T>` and `ArrayPool<T>`
- **Multi-Target**: Supports .NET Standard 2.0/2.1, .NET 8.0, and .NET 10.0
- **Format Support**:
  - Audio: MP3 (ID3v1/ID3v2), FLAC, OGG Vorbis, WAV (RIFF INFO/ID3v2), AIFF (read)
  - Planned: MP4/M4A, ASF/WMA, APE, Opus, DSF

## Installation

```bash
dotnet add package TagLibSharp2
```

Or build from source:

```bash
git clone https://github.com/decriptor/TagLibSharp2.git
cd tagsharp
dotnet build
```

## Quick Start

```csharp
using TagLibSharp2.Mpeg;
using TagLibSharp2.Xiph;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Aiff;

// Read MP3 tags (prefers ID3v2, falls back to ID3v1)
var result = Mp3File.ReadFromFile("song.mp3");
if (result.IsSuccess)
{
    var mp3 = result.File!;
    Console.WriteLine($"{mp3.Title} by {mp3.Artist}");

    // Modify and save
    mp3.Title = "New Title";
    mp3.SaveToFile("song.mp3", File.ReadAllBytes("song.mp3"));
}

// FLAC and Ogg Vorbis work the same way
var flac = FlacFile.ReadFromFile("song.flac").File;
var ogg = OggVorbisFile.ReadFromFile("song.ogg").File;

// WAV files support both RIFF INFO and ID3v2 tags
WavFile.TryParse(new BinaryData(File.ReadAllBytes("song.wav")), out var wav);
Console.WriteLine($"WAV: {wav.Title} - {wav.AudioProperties?.Duration}");

// AIFF files (read-only, includes audio properties)
AiffFile.TryParse(new BinaryData(File.ReadAllBytes("song.aiff")), out var aiff);
Console.WriteLine($"AIFF: {aiff.AudioProperties?.SampleRate}Hz");

// Async support for high-throughput scenarios
var asyncResult = await Mp3File.ReadFromFileAsync("song.mp3");
```

See the [examples](examples/) directory for more comprehensive usage patterns.

## Building

```bash
git clone https://github.com/decriptor/TagLibSharp2.git
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
  - [x] Text frames (TIT2, TPE1, TALB, TYER, TDRC, TCON, TRCK, TPE2, TPOS, TCOM, TBPM, TENC, TSSE, TIT1, TIT3, TPE4, TKEY, TMOO, TMED, TLAN)
  - [x] Involved people frames (TIPL, TMCL, IPLS) for musician credits
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

### Phase 6: Extended Metadata & High-Level APIs ✅
- [x] ID3v2 TXXX (user-defined text) frames for custom metadata
- [x] ReplayGain tag support (ID3v2 and Vorbis Comments)
- [x] MusicBrainz ID support (ID3v2 and Vorbis Comments)
- [x] Mp3File high-level API for unified ID3v1/ID3v2 access
- [x] Ogg Vorbis file write operations
- [x] Lyrics (USLT frame) with multi-language support
- [x] UFID (Unique File Identifier) for MusicBrainz Recording IDs
- [x] Extended properties: Conductor, Copyright, Compilation, TotalTracks/TotalDiscs, PerformersRole
- [x] Encoding metadata: EncodedBy, EncoderSettings
- [x] Track info: Grouping, Subtitle, Remixer, InitialKey, Mood, Language
- [x] Release data: MediaType, Barcode, CatalogNumber
- [x] Sort fields: ComposerSort
- [x] Tagging metadata: DateTagged, Description, AmazonId
- [x] Extended MusicBrainz: WorkId, DiscId, ReleaseStatus, ReleaseType, ReleaseCountry
- [x] TagTypes flags enum for tag format identification
- [x] Array properties: Performers[], AlbumArtists[], Composers[], Genres[]
- [x] IPicture interface and Pictures[] property on base Tag class

### Phase 7: RIFF/WAV Support ✅
- [x] RIFF container parsing with chunk navigation
- [x] WAV format chunk (fmt) for audio properties
- [x] RIFF INFO tags (INAM, IART, IPRD, etc.)
- [x] ID3v2 chunk support in WAV files
- [x] WAV file write operations

### Phase 8: AIFF Support ✅
- [x] FORM container parsing (IFF-style, big-endian)
- [x] COMM chunk with 80-bit extended float sample rate
- [x] SSND chunk detection
- [x] ID3 chunk support for metadata
- [x] AIFC (compressed) format detection
- [x] ExtendedFloat utility for 80-bit IEEE 754

### Future
- [ ] MP4/M4A (AAC/ALAC) with iTunes atoms
- [ ] Opus audio format
- [ ] DSF (DSD) format
- [ ] ASF/WMA format
- [ ] APE tags for WavPack/Musepack
- [ ] AIFF write support

## Documentation

- [Migration Guide](docs/MIGRATION-FROM-TAGLIB.md) - Migrating from TagLib#
- [Architecture Overview](docs/ARCHITECTURE.md) - Design principles and allocation behavior
- [Core Types Reference](docs/CORE-TYPES.md) - Complete API documentation
- [Building Guide](docs/BUILDING.md) - Build instructions and requirements
- [Examples](examples/) - Working code samples

## Contributing

Contributions are welcome! Please read the [contributing guidelines](CONTRIBUTING.md) before submitting PRs.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
