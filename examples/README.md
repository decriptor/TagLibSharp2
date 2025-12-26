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

## Future Examples

As TagLibSharp2 development progresses, additional examples will be added:

- **Id3Reading** - Reading ID3v1 and ID3v2 tags from MP3 files
- **Id3Writing** - Writing and modifying ID3 tags
- **FlacMetadata** - Working with FLAC metadata and Vorbis comments
- **BatchProcessing** - Processing multiple files efficiently
