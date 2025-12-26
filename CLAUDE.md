# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**TagLibSharp2** is a clean-room rewrite of media file metadata handling, licensed under MIT.

This is NOT a fork or derivative of TagLib#. Implementations must be based solely on format specifications, not existing code.

## Build Commands

```bash
dotnet build                    # Build solution
dotnet test                     # Run all tests
dotnet test --filter "Name~Id3" # Run specific tests
```

## Clean Room Development Rules

**CRITICAL**: This project must avoid any GPL/LGPL contamination.

1. **DO NOT** reference TagLib# source code when implementing features
2. **DO** reference official format specifications:
   - ID3: https://id3.org/Developer%20Information
   - Vorbis: https://xiph.org/vorbis/doc/
   - FLAC: https://xiph.org/flac/format.html
   - MPEG-4: ISO 14496-12/14
3. **OK** to use TagLib# test files as test inputs (they're just data)
4. **OK** to design similar public APIs (APIs aren't copyrightable)

## Architecture

### Core Types

```
TagLibSharp2/
├── Core/
│   ├── BinaryData.cs       # Immutable binary data with Span<T> support
│   ├── MediaFile.cs        # Factory + base class
│   └── Tag.cs              # Abstract tag interface
├── Id3/
│   ├── Id3v1Tag.cs         # ID3v1 implementation
│   └── Id3v2/
│       ├── Id3v2Tag.cs     # ID3v2 container
│       ├── Frame.cs        # Base frame
│       └── Frames/         # Frame implementations
├── Xiph/
│   ├── VorbisComment.cs    # Vorbis comment block
│   └── FlacFile.cs         # FLAC container
└── Properties/
    └── MediaProperties.cs  # Duration, bitrate, etc.
```

### Design Principles

1. **Immutable by default**: ByteVector and parsed data should be immutable
2. **Span<T> first**: Use `ReadOnlySpan<byte>` for parsing, avoid allocations
3. **Async I/O**: All file operations should have async variants
4. **Nullable annotations**: Full nullable reference type support
5. **No exceptions for validation**: Use result types for parsing errors

## Code Style

- File-scoped namespaces
- Primary constructors where appropriate
- Expression-bodied members for simple properties
- Pattern matching for type checks
- `is null` / `is not null` (not `== null`)

## Target Frameworks

- `netstandard2.0` - Broadest compatibility
- `netstandard2.1` - Better Span support
- `net8.0` - LTS
- `net10.0` - Current

## Specification References

### ID3v1
- Fixed 128-byte footer at end of file
- Fields: Title(30), Artist(30), Album(30), Year(4), Comment(30), Genre(1)
- ID3v1.1: Last 2 bytes of comment = track number if byte[28]=0

### ID3v2
- Header: "ID3" + version(2) + flags(1) + syncsafe size(4)
- v2.3: 4-byte frame IDs, big-endian sizes
- v2.4: 4-byte frame IDs, syncsafe sizes, UTF-8 support

### FLAC
- Magic: "fLaC" (4 bytes)
- Metadata blocks: type(1) + size(3) + data
- Block types: STREAMINFO(0), PADDING(1), APPLICATION(2), SEEKTABLE(3), VORBIS_COMMENT(4), CUESHEET(5), PICTURE(6)

### Vorbis Comment
- Vendor string (length-prefixed UTF-8)
- Comment count (32-bit LE)
- Comments: length(4) + "KEY=value" (UTF-8)
