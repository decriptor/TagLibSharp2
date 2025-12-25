# TagSharp

A modern .NET library for reading and writing metadata in media files.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- **Modern .NET**: Built for .NET 8+ with nullable reference types, `Span<T>`, and async support
- **MIT License**: Permissive licensing for all use cases
- **Format Support** (planned):
  - Audio: MP3 (ID3v1/ID3v2), FLAC, OGG Vorbis, WAV
  - Images: JPEG, PNG, TIFF (EXIF/XMP)
  - Video: MP4, MKV

## Installation

```bash
dotnet add package TagSharp
```

## Quick Start

```csharp
using TagSharp;

// Read metadata
var file = MediaFile.Open("song.mp3");
Console.WriteLine($"Title: {file.Tag.Title}");
Console.WriteLine($"Artist: {file.Tag.Artist}");
Console.WriteLine($"Duration: {file.Properties.Duration}");

// Write metadata
file.Tag.Title = "New Title";
file.Tag.Artist = "New Artist";
await file.SaveAsync();
```

## Building

```bash
git clone https://github.com/decriptor/tagsharp.git
cd tagsharp
dotnet build
dotnet test
```

## Project Status

This is a clean-room rewrite of media tagging functionality, designed from specifications rather than existing implementations.

### Phase 1: Core Infrastructure
- [ ] ByteVector (binary data handling with Span<T>)
- [ ] File abstraction layer
- [ ] Tag base classes

### Phase 2: ID3 Support
- [ ] ID3v1 (id3.org specification)
- [ ] ID3v2.3/2.4 (id3.org specification)

### Phase 3: Xiph Formats
- [ ] Vorbis Comments (xiph.org specification)
- [ ] FLAC metadata blocks (xiph.org specification)

## Contributing

Contributions are welcome! Please read the contributing guidelines before submitting PRs.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
