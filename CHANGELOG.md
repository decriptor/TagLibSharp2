# Changelog

All notable changes to TagLibSharp2 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### AIFF Write Support
- `AiffFile.Render` for serializing AIFF files to binary data
- `AiffFile.SaveToFile` and `SaveToFileAsync` for atomic file saves
- `AiffChunk.Render` for chunk serialization with proper padding
- Settable `Tag` property on `AiffFile` for modifying ID3v2 metadata
- All existing chunks preserved during render (COMM, SSND, ANNO, etc.)

#### Metadata Preservation
- FLAC: Preserve SEEKTABLE and APPLICATION blocks during render
  - `FlacPreservedBlock` struct for block storage
  - `PreservedBlocks` property on `FlacFile`
- WAV: Preserve all unknown chunks (fact, cue, smpl, etc.) during render
  - Only LIST INFO and id3 chunks are replaced; all others preserved

#### AIFC Compression Support
- `AiffAudioProperties.CompressionType` for AIFC compression type (e.g., "NONE", "sowt")
- `AiffAudioProperties.CompressionName` for human-readable compression description
- Standard AIFF files return null for compression properties

#### Documentation
- Format support matrix in README showing feature coverage per format
- Dedicated NuGet README with quick start guide

#### BWF (Broadcast Wave Format) Support
- `BextTag` class for parsing and writing bext chunks
  - Description, Originator, OriginatorReference fields
  - OriginationDate and OriginationTime for timestamps
  - TimeReference for sample-accurate synchronization
  - UMID (Unique Material Identifier) for Version 1+
  - CodingHistory for production chain tracking
- `WavFile.BextTag` property for accessing bext metadata

#### WAVEFORMATEXTENSIBLE Support
- `WavAudioPropertiesParser.ParseExtended` for parsing extensible fmt chunks
- `WavExtendedProperties` struct with:
  - ValidBitsPerSample for actual signal precision
  - ChannelMask for surround sound speaker positions
  - SubFormat for actual audio format (PCM, IEEE Float, A-Law, mu-Law)
- `WavChannelMask` constants for speaker positions (5.1, 7.1, etc.)
- `WavSubFormat` enum for format identification
- `WavFile.ExtendedProperties` property for accessing extended audio info

#### Ogg CRC Validation
- Optional `validateCrc` parameter on `OggVorbisFile.Read`, `ReadFromFile`, `ReadFromFileAsync`
- When enabled, validates CRC-32 checksums on each Ogg page
- Disabled by default for performance (existing behavior)
- Useful for detecting file corruption in critical applications

#### Security
- Integer overflow protection for malformed chunk sizes
  - AIFF/AIFC: Reject chunks claiming sizes > int.MaxValue
  - RIFF/WAV: Reject chunks and INFO fields claiming sizes > int.MaxValue
  - Defense-in-depth checks added to AiffChunk, RiffChunk, and RiffInfoTag

### Changed
- Test count increased from 1528 to 1564

## [0.1.0] - 2025-12-26

### Added

#### Core Infrastructure
- `BinaryData` immutable binary wrapper with Span<T> support
- `BinaryDataBuilder` mutable builder with ArrayPool integration for reduced GC pressure
- Big-endian and little-endian integer parsing (16, 24, 32, 64-bit)
- Syncsafe integer support for ID3v2 format
- String encoding/decoding (Latin-1, UTF-8, UTF-16 with BOM detection)
- CRC computation (CRC-8, CRC-16-CCITT, CRC-32)
- Pattern matching operations (IndexOf, Contains, StartsWith, EndsWith)
- Hex string conversion
- Abstract `Tag` base class with standard metadata properties
- Abstract `Picture` base class with picture type support
- `TagReadResult<T>` for safe parsing without exceptions
- Multi-target framework support (netstandard2.0, netstandard2.1, net8.0, net10.0)

#### ID3 Support
- ID3v1 tag reading and writing
  - ID3v1.0 and v1.1 detection
  - Genre lookup table (129 standard genres)
  - Track number support (v1.1)
- ID3v2 tag support (versions 2.3 and 2.4)
  - Header parsing with flags and syncsafe sizes
  - Text frames (TIT2, TPE1, TALB, TYER, TDRC, TCON, TRCK, TPE2, TPOS, TCOM, TBPM, TENC, TSSE, TIT1, TIT3, TPE4, TKEY, TMOO, TMED, TLAN)
  - Picture frames (APIC) with description and multiple picture types
  - Multiple text encodings (Latin-1, UTF-8, UTF-16 BE/LE)
  - Tag rendering with proper padding management
  - Extended header support (v2.3 and v2.4)
  - AlbumArtist, DiscNumber, Composer, and BeatsPerMinute properties

#### Xiph Format Support
- FLAC file container
  - Magic number validation ("fLaC")
  - Metadata block parsing (STREAMINFO, VORBIS_COMMENT, PICTURE)
  - CRC validation
- Vorbis Comment support
  - Case-insensitive field handling
  - UTF-8 encoding throughout
  - Multiple values per field
  - Standard field mappings (TITLE, ARTIST, ALBUM, DATE, GENRE, TRACKNUMBER)
  - Extended fields (ALBUMARTIST, DISCNUMBER, COMPOSER)
  - Picture embedding (base64-encoded METADATA_BLOCK_PICTURE)
- Ogg Vorbis file support
  - Ogg page parsing with segment tables
  - CRC-32 validation
  - Vorbis comment extraction from packets

#### File I/O
- `IFileSystem` abstraction for testability
- `DefaultFileSystem` singleton for real file operations
- `FileHelper.SafeReadAllBytes` with consistent error handling
- `FileHelper.SafeReadAllBytesAsync` with cancellation support
- `FlacFile.ReadFromFileAsync` and `OggVorbisFile.ReadFromFileAsync`
- `AtomicFileWriter` for safe file writes (temp file + rename pattern)
- `FlacFile.SaveToFile` and `FlacFile.SaveToFileAsync` for FLAC writing

#### Media Properties
- `IMediaProperties` interface for audio duration and quality information
- `AudioProperties` struct with Duration, Bitrate, SampleRate, Channels, BitsPerSample
- FLAC media properties from STREAMINFO block
- Ogg Vorbis media properties from identification header

#### ID3v2 Comment Frame
- COMM frame support for ID3v2 comments
- Language code (ISO 639-2) and description support
- Multiple encodings (Latin-1, UTF-8, UTF-16)

#### ID3v2 User-Defined Text Frames
- TXXX frame support for custom metadata fields
- Multiple text encodings (Latin-1, UTF-8, UTF-16)
- GetUserText/SetUserText API for named fields

#### Extended Metadata Support
- ReplayGain tag support for ID3v2 (via TXXX frames)
  - Track gain/peak and album gain/peak
- ReplayGain tag support for Vorbis Comments
  - REPLAYGAIN_TRACK_GAIN, REPLAYGAIN_TRACK_PEAK, REPLAYGAIN_ALBUM_GAIN, REPLAYGAIN_ALBUM_PEAK
- MusicBrainz ID support for ID3v2 (via TXXX frames)
  - Track ID, Release ID, Artist ID, Release Group ID, Album Artist ID
- MusicBrainz ID support for Vorbis Comments
  - MUSICBRAINZ_TRACKID, MUSICBRAINZ_ALBUMID, MUSICBRAINZ_ARTISTID, MUSICBRAINZ_RELEASEGROUPID, MUSICBRAINZ_ALBUMARTISTID

#### Lyrics & Additional Metadata
- USLT (Unsynchronized Lyrics) frame support for ID3v2
  - Multi-language support with ISO 639-2 language codes
  - Description field for multiple lyrics per file
  - Full encoding support (Latin-1, UTF-8, UTF-16)
- UFID (Unique File Identifier) frame support for ID3v2
  - MusicBrainz Recording ID via canonical UFID frame
  - Binary and string identifier formats
- Extended tag properties (ID3v2 and Vorbis Comments)
  - Conductor (TPE3 frame)
  - Copyright (TCOP frame)
  - Compilation flag (TCMP frame)
  - TotalTracks and TotalDiscs properties
  - EncodedBy (TENC / ENCODED-BY) and EncoderSettings (TSSE / ENCODER)
  - Grouping (TIT1 / GROUPING), Subtitle (TIT3 / SUBTITLE), Remixer (TPE4 / REMIXER)
  - InitialKey (TKEY / KEY), Mood (TMOO / MOOD), Language (TLAN / LANGUAGE)
  - MediaType (TMED / MEDIA), Barcode (TXXX:BARCODE / BARCODE), CatalogNumber (TXXX:CATALOGNUMBER / CATALOGNUMBER)

#### Performer Role Support
- PerformersRole property for musician credits
  - ID3v2 TMCL (Musician Credits List) frame support
  - ID3v2 TIPL (Involved People List) frame support
  - ID3v2 IPLS (v2.3 Involved People) frame support
  - Vorbis Comment PERFORMER_ROLE field support
  - InvolvedPeopleFrame class for parsing/rendering role-person pairs

#### TagTypes Enum & Array Properties
- `TagTypes` flags enum for identifying tag format types (Id3v1, Id3v2, Xiph, Apple, Asf, etc.)
- `TagType` abstract property on base `Tag` class
- Array properties for multi-value metadata:
  - `Performers[]` - Multiple artists/performers with null-separator support (ID3v2) and multi-field support (Vorbis)
  - `AlbumArtists[]` - Multiple album artists
  - `Composers[]` - Multiple composers
  - `Genres[]` - Multiple genres
- `IPicture` interface for picture abstraction (TagLib# API compatibility)
- `Pictures` property returns `IPicture[]` for format-agnostic picture access
- Format-specific properties renamed: `PictureFrames` (ID3v2) and `PictureBlocks` (Vorbis)

#### Sort Fields & TagLib# Parity
- ComposerSort (TSOC / COMPOSERSORT) - Sort order for composer names
- DateTagged (TDTG / DATETAGGED) - ISO 8601 tagging timestamp (ID3v2.4 only)
- Description (TXXX:DESCRIPTION / DESCRIPTION) - Content synopsis
- AmazonId (TXXX:ASIN / ASIN) - Amazon Standard Identification Number
- MusicIpId (TXXX:MusicIP PUID / MUSICIP_PUID) - Obsolete, deprecated in favor of AcoustID
- Extended MusicBrainz identifiers (ID3v2 and Vorbis Comments):
  - WorkId - Musical work/composition identifier
  - DiscId - CD table of contents hash
  - ReleaseStatus - official, promotional, bootleg, pseudo-release
  - ReleaseType - album, single, ep, compilation, etc.
  - ReleaseCountry - ISO 3166-1 alpha-2 country code

#### High-Level MP3 API
- `Mp3File` class for unified ID3v1/ID3v2 access
- Automatic ID3v2 preference with ID3v1 fallback
- Read/write support with atomic file saves
- Async file operations with cancellation support

#### Ogg Vorbis Write Support
- `OggVorbisFile.Render` for rebuilding files with updated metadata
- `SaveToFile` and `SaveToFileAsync` for atomic file saves
- Proper Ogg page segment table handling for multi-packet pages

#### Examples
- BasicUsage example demonstrating BinaryData operations
- TagOperations example showing all tag reading/writing features

#### Project Infrastructure
- GitHub Actions CI workflow for cross-platform builds (Ubuntu, Windows, macOS)
- Dependabot configuration for automated dependency updates
- Comprehensive test suite (1015 tests)
- Malformed input test suite for security and robustness
- Clean-room implementation (no TagLib# code)

### Changed
- `BinaryData(byte[])` constructor now copies the array to ensure true immutability

[0.1.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.1.0
