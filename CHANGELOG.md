# Changelog

All notable changes to TagLibSharp2 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.0] - 2026-01-03

### Added

#### ASF/WMA Format Support
- `AsfFile` class for reading and writing ASF/WMA audio files
- Header Object parsing with GUID-based child object navigation
- Content Description object for Title, Artist, Copyright, Description, Rating
- Extended Content Description for Album, Year, Genre, Track, and custom fields
- Stream Properties for audio properties (bitrate, sample rate, channels, bits per sample)
- File Properties for duration extraction
- Full read/write round-trip support with atomic file saves
- Async file I/O with cancellation support
- Result types: `AsfFileReadResult`, `AsfGuidParseResult`

#### Musepack Format Support
- `MusepackFile` class for reading Musepack audio files
- SV7 and SV8 stream version detection
- Audio properties (sample rate, channels, duration, bitrate)
- APE tag support for metadata with full read/write
- Result type: `MusepackFileParseResult`

#### WavPack Format Support
- `WavPackFile` class for reading WavPack audio files
- Block header parsing with format flags
- Sample rate table lookup (15 standard rates)
- Duration calculation from total samples and sample rate
- APE tag support for metadata with full read/write
- Result type: `WavPackFileParseResult`

#### Monkey's Audio Format Support
- `MonkeysAudioFile` class for reading Monkey's Audio files
- Both old format (≤3.97) and new format (≥3.98) header parsing
- STREAMINFO extraction (sample rate, channels, bits per sample)
- Compression level detection (Fast, Normal, High, Extra High, Insane)
- APE tag support for metadata with full read/write
- Result type: `MonkeysAudioFileParseResult`

#### Ogg FLAC Format Support
- `OggFlacFile` class for reading Ogg FLAC audio files
- Ogg container with embedded FLAC stream parsing
- STREAMINFO extraction (sample rate, channels, bits per sample)
- Vorbis Comment support with full read/write
- Duration calculation from total samples
- Result type: `OggFlacFileParseResult`

#### DFF (DSDIFF) Format Support
- `DffFile` class for reading DFF audio files (Philips DSDIFF format)
- IFF-based FRM8 container with big-endian byte order
- FVER chunk parsing for format version detection (DSDIFF 1.5)
- PROP chunk parsing with SND properties: FS (sample rate), CHNL (channels), CMPR (compression)
- DSD and DST compression type detection
- `DffAudioProperties` with DSD-specific information
- ID3v2 tag support as unofficial extension
- Result type: `DffFileParseResult`
- Full read/write round-trip support for metadata

#### DSF/DFF Write Support
- `DsfFile.SaveToFile` and `SaveToFileAsync` for writing DSF files
- `DffFile.SaveToFile` and `SaveToFileAsync` for writing DFF files
- ID3v2 metadata modifications preserved through round-trip

#### APE Tag v2 Support
- `ApeTag` class for reading and writing APE v2 tags
- APE item types: Text (UTF-8), Binary, External (locators)
- Read-only flag support per APE specification
- `ApeTagFooter` and `ApeTagHeader` parsing with version detection
- `ApeTagItem` for individual tag items with flags
- Result types: `ApeTagFooterParseResult`, `ApeTagHeaderParseResult`, `ApeTagItemParseResult`, `ApeTagParseResult`
- Full read/write round-trip support
- Standard metadata mappings: Title, Artist, Album, Track, Genre, Date, Comment
- Extended metadata: ReplayGain, MusicBrainz IDs, Performer roles

#### DSF (DSD Stream File) Format Support
- `DsfFile` class for reading DSF audio files (Sony DSD format)
- DSD64 (2.8MHz), DSD128 (5.6MHz), DSD256 (11.2MHz), DSD512 (22.4MHz) sample rate detection
- `DsfDsdChunk` for DSD header with total file size and metadata pointer
- `DsfFmtChunk` for format specification (channels, sample rate, bits per sample)
- `DsfDataChunk` for audio data boundaries
- `DsfAudioProperties` with DSD-specific information
- ID3v2 tag support at end of file (per DSF specification)
- Result types: `DsfDsdChunkParseResult`, `DsfFmtChunkParseResult`, `DsfDataChunkParseResult`, `DsfFileParseResult`
- Channel type detection: Mono, Stereo, 3-channel, Quad, 4-channel, 5-channel, 5.1 surround

#### IDisposable Pattern
- `BinaryDataBuilder` now implements `IDisposable` for proper resource cleanup
- Automatic return of ArrayPool buffers on dispose
- `using` statement support for deterministic cleanup

#### Large File Support
- Validated file operations with files >4GB
- Comprehensive test coverage for large file scenarios

#### MediaFile Factory Updates
- `MediaFormat.Asf` enum value for ASF/WMA detection
- `MediaFormat.Musepack` enum value for Musepack detection
- `MediaFormat.WavPack` enum value for WavPack detection
- `MediaFormat.MonkeysAudio` enum value for Monkey's Audio detection
- `MediaFormat.OggFlac` enum value for Ogg FLAC detection
- Automatic format detection for all new formats

### Changed
- Test count increased from 2272 to 3175 (+903 tests)
- Code coverage: 90.1% line coverage, 77.6% branch coverage
- 15 audio formats now supported with full read/write

## [0.4.0] - 2025-12-31

### Added

#### MP4/M4A Format Support
- `Mp4File` class for reading and writing MP4/M4A audio files
- ISO 14496-12 (ISOBMFF) and ISO 14496-14 compliant box parsing
- iTunes-style metadata (moov → udta → meta → ilst atoms)
- Full read/write round-trip support with atomic file saves
- 197 comprehensive tests covering parsing, rendering, and edge cases

#### MP4 Box Infrastructure
- `Mp4Box` class for hierarchical box (atom) parsing
- `Mp4BoxParser` for recursive child box parsing
- `Mp4FullBox` support for version and flags fields
- `Mp4DataAtom` for typed data atoms (text, integer, binary)
- `Mp4BoxReadResult` result type with error context
- Extended box size support (size=1 with 64-bit size field)

#### MP4 Audio Properties
- `Mp4AudioPropertiesParser` for extracting audio information
- Duration from mvhd (movie header) with timescale conversion
- Sample rate from stsd (sample description) or mdhd fallback
- High sample rate support (>65535 Hz via mdhd timescale)
- Channel count, bits per sample (for lossless codecs)
- Bitrate extraction from esds (Elementary Stream Descriptor)

#### MP4 Audio Codec Detection
- `Mp4AudioCodec` enum: AAC, ALAC, FLAC, Opus, AC-3, E-AC-3, Unknown
- Codec-specific parsing for accurate properties
- `EsdsParser` for AAC decoder configuration
- `AlacMagicCookie` parsing for ALAC parameters

#### Standard iTunes Metadata Atoms
- Text atoms: Title (©nam), Artist (©ART), Album (©alb), Genre (©gen)
- Date atoms: Year (©day)
- Comment atom: ©cmt
- Composer atom: ©wrt
- Album artist atom: aART
- Track/disc numbers: trkn and disk (8-byte binary format)
- BPM: tmpo (16-bit integer)
- Gapless playback: pgap (boolean)
- Compilation flag: cpil (boolean)
- Encoder: ©too
- Copyright: cprt

#### Sort Order Atoms
- AlbumSort (soal), ArtistSort (soar), TitleSort (sonm)
- AlbumArtistSort (soaa), ComposerSort (soco)

#### Classical Music Metadata
- Work name: ©wrk
- Movement name: ©mvn
- Movement number: ©mvi (integer)
- Movement count: ©mvc (integer)
- Show movement flag: shwm (boolean)

#### Cover Art Support
- `Mp4Picture` class implementing `IPicture` interface
- JPEG (type code 13) and PNG (type code 14) support
- Multiple pictures per file via covr atom
- AddPicture/RemovePictures API on Mp4File

#### Freeform Metadata (---- atoms)
- Full support for mean/name/data structure
- GetFreeformTag/SetFreeformTag API for arbitrary metadata

##### MusicBrainz Identifiers (org.musicbrainz namespace)
- Track ID, Album ID, Artist ID, Album Artist ID
- Release Group ID, Work ID, Recording ID, Disc ID
- Release Status, Release Type, Release Country

##### AcoustID (com.apple.iTunes namespace)
- AcoustId Id
- AcoustId Fingerprint

##### ReplayGain (com.apple.iTunes namespace)
- Track gain and peak (replaygain_track_gain/peak)
- Album gain and peak (replaygain_album_gain/peak)

##### R128 Loudness (com.apple.iTunes namespace)
- Track gain (R128_TRACK_GAIN) - Q7.8 fixed-point
- Album gain (R128_ALBUM_GAIN) - Q7.8 fixed-point
- Convenience properties: R128TrackGainDb, R128AlbumGainDb (decibels)

##### DJ and Remix Metadata
- Initial key (initialkey)
- Remixer (REMIXER)
- Mood (MOOD)
- Subtitle (SUBTITLE)

##### Collector Metadata
- Barcode (BARCODE)
- Catalog number (CATALOGNUMBER)
- Amazon ID (ASIN)

##### Library Management
- Date tagged (date_tagged)
- Language (LANGUAGE)
- Media type (MEDIA)

##### Additional Identifiers
- ISRC (ISRC)
- Conductor (CONDUCTOR)
- Original year (ORIGINAL YEAR)

#### MediaFile Factory Updates
- `MediaFormat.Mp4` enum value
- Automatic MP4 detection by ftyp box
- `.m4a`, `.mp4`, `.m4b`, `.m4p`, `.m4v` file extension support

### Changed
- Test count increased from 2077 to 2272 (+195 tests)

## [0.3.0] - 2025-12-30

### Added

#### Ogg Opus Format Support
- `OggOpusFile` class for reading and writing Opus audio files
- OpusHead identification header parsing (RFC 7845)
  - Version, channels, pre-skip, input sample rate, output gain
  - Channel mapping family support
- OpusTags (Vorbis Comment) metadata support (no framing bit, per RFC 7845)
- `OggOpusFileReadResult` result type with error context
- Full read/write round-trip support with atomic file saves

#### Audio Properties for Opus
- `AudioProperties.FromOpus()` factory method
- Duration calculation from granule position (always 48kHz output)
- Pre-skip compensation for accurate duration
- Bitrate calculation from file size

#### MediaFile Factory Updates
- `MediaFormat.Opus` enum value
- Automatic Opus vs Vorbis detection by inspecting first Ogg packet
- `.opus` file extension support

#### R128 Loudness Convenience Properties
- `R128TrackGainDb` and `R128AlbumGainDb` properties on `Tag` base class
- Convert Q7.8 fixed-point integers to/from decibels as doubles
- Uses `CultureInfo.InvariantCulture` for locale-independent parsing

### Fixed

#### RFC 3533 Compliance (Ogg Container)
- Add segment table overflow validation in `OggPageHelper.BuildOggPage`
  - Throws `ArgumentException` when packets require >255 segments
  - Prevents corrupt Ogg files with large embedded artwork
- Add bounds check in `FindLastGranulePosition` for truncated pages
- Add `BuildMultiPagePacket` helper for large metadata spanning multiple pages

#### RFC 7845 Compliance (Opus Codec)
- Add channel mapping family 1 validation (1-8 channels per §5.1.1.2)
- Existing OpusHead minimum size validation (19 bytes) now tested

#### netstandard2.0 Compatibility
- Replace C# 12 collection expressions `[]` with explicit `new List<T>()`
- Fix `#if NET8_0_OR_GREATER` to `#if NET5_0_OR_GREATER` for CollectionsMarshal

#### R128 Gain Handling
- Fix integer overflow in `R128TrackGainDb` and `R128AlbumGainDb` setters for extreme values
- Add `ClampToQ78` helper to safely clamp dB values to Q7.8 format range (-128 to +127.99 dB)

#### Vorbis Validation
- Add validation for invalid `vorbis_version` in identification header (must be 0 per spec)
- Return explicit error for Vorbis files with zero sample rate or channel count

#### Security Hardening
- Add 16 MB max packet size limit in `OggPageHelper.ExtractHeaderPackets` to prevent DoS
- Add stream/coupled count validation for Opus mapping families 1 & 255 per RFC 7845 §5.1.1.2

### Changed
- **Refactor**: `OggVorbisFile` now uses shared `OggPageHelper` code
  - Eliminates ~160 lines of duplicate code
  - `BuildOggPage` now includes segment overflow validation
  - `FindLastGranulePosition` properly uses EOS flag per RFC 3533
- Add `SaveToFileAsync` convenience overloads to `OggVorbisFile`
- Change `OggPageWithSegmentsResult.Segments` to `IReadOnlyList<T>` for encapsulation
- Standardize magic bytes to char cast syntax for readability
- Test count increased from 1939 to 2077 (+138 tests)

## [0.2.1] - 2025-12-29

### Added

#### Error Context for File Parsing
- `WavFileReadResult` struct with `IsSuccess`, `File`, and `Error` properties
- `AiffFileReadResult` struct with `IsSuccess`, `File`, and `Error` properties
- `WavFile.Read(ReadOnlySpan<byte>)` returns result type with error context
- `AiffFile.Read(ReadOnlySpan<byte>)` returns result type with error context
- `AiffFile.ReadFromFile` and `ReadFromFileAsync` methods for direct file loading

#### Test Coverage
- `PolyfillsTests.cs` - Hex conversion and Latin1 encoding tests
- `OggCrcTests.cs` - CRC-32 calculation and validation tests
- `Id3v1GenreTests.cs` - Genre lookup for all 192 genres
- `DocExamplesCompileTests.cs` - Verify documentation code examples compile
- `ResultTypeEqualityTests.cs` - IEquatable tests for 27 result types
- `StructEqualityTests.cs` - Equality tests for SyncLyricsItem, FlacPreservedBlock, etc.

#### Documentation
- Fix Cookbook.md: Correct SyncLyricsFrame API (SyncLyricsItem, SyncLyricsType)
- Fix Cookbook.md: Correct ChapterFrame API (StartTimeMs, EndTimeMs)
- Update MILESTONES.md and ROADMAP.md with current status

### Changed
- `WavFile.ReadFromFile` and `ReadFromFileAsync` now return `WavFileReadResult`
- `AiffFile.ReadFromFile` and `ReadFromFileAsync` now return `AiffFileReadResult`
- Improved documentation for `Polyfills.Replace` StringComparison limitation
- Test count increased from 1756 to 1939 (+183 tests)

## [0.2.0] - 2025-12-29

### Added

#### ID3v2.2 Legacy Support
- Full ID3v2.2 (3-character frame ID) parsing support
- Complete v2.2 → v2.3/v2.4 frame ID mapping (66 mappings)
- Proper 3-byte big-endian size handling for v2.2 frames

#### ID3v2 Unsynchronization
- Global unsynchronization support (tag-level, v2.3/v2.4)
- Per-frame unsynchronization support (v2.4 frame flag)
- Proper 0xFF 0x00 byte sequence removal during parsing
- Two-pass algorithm for efficient memory usage

#### ID3v2 Frame Flags Processing
- Compression support with zlib decompression (v2.3 and v2.4)
- Grouping identity flag handling (skips group ID byte)
- Data length indicator parsing (v2.4 syncsafe 4-byte prefix)
- Encryption flag detection (content preserved as-is)

#### FLAC MD5 Audio Signature
- `FlacFile.AudioMd5Signature` property for 128-bit MD5 hash of unencoded audio
- `FlacFile.AudioMd5SignatureHex` for hex string representation
- `FlacFile.HasAudioMd5Signature` to detect if encoder computed the hash
- Essential for bit-perfect archive verification

#### Picture Support for WAV and AIFF
- `WavFile.Pictures` and `WavFile.HasPictures` via embedded ID3v2 tag
- `AiffFile.Pictures` and `AiffFile.HasPictures` via embedded ID3v2 tag
- `CoverArt` convenience property for primary album art
- Full PictureType support (FrontCover, BackCover, etc.)

#### SaveToFile Convenience Overloads
- `Mp3File.SaveToFile(path)` - saves to specified path
- `FlacFile.SaveToFile(path)` - saves to specified path
- `OggVorbisFile.SaveToFile(path)` - saves to specified path
- `WavFile.SaveToFile(path)` - saves to specified path
- `AiffFile.SaveToFile(path)` - saves to specified path
- All with async variants accepting CancellationToken

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
- Test count increased from 1528 to 1658 (+130 tests)

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

[0.5.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.5.0
[0.4.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.4.0
[0.3.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.3.0
[0.2.1]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.2.1
[0.2.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.2.0
[0.1.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.1.0
