# Changelog

All notable changes to TagLibSharp2 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Breaking Changes

- **Changed**: `AiffFile.Tag` is now a get-only `Tag?` view over the ID3 chunk and the native AIFF text chunks (it was a settable `Id3v2Tag?`). Assign or read the ID3 tag through the new `AiffFile.Id3v2Tag` property; code that assigned `AiffFile.Tag` or used ID3-only members on it no longer compiles. `AiffFile.Tag.Render()` throws `NotSupportedException`, as the other combined views do; render the `AiffFile` instead
- **Changed**: `Id3v2Tag.IsEmpty` is false when the tag holds any frame, including lyrics, ratings, pictures, comments, user text, URLs and private frames. It used to check only the basic fields, so WAV and AIFF saves dropped an ID3 chunk that held only, for example, a composer, a publisher or a BPM

### Added

- `MatroskaFile.Tracks` lists the first 4096 known-size `TrackEntry` elements as `MatroskaTrack` objects, and `MatroskaFile.IsTrackListTruncated` tells when a file had more. Each track has `TrackNumber`, `TrackUid`, `TrackType`, `CodecId`, `IsDefault`, and `IsEnabled` (RFC 9559 §5.1.4.1).
- `MatroskaTrack.IsEnabled` is written back on save. A stored `FlagEnabled` is rewritten in place. A missing one is added to the `TrackEntry` when the `Void` right after `Tracks` has room, so no element moves; otherwise the save fails and nothing is written. Adding a FlagEnabled (minver 2) also makes the EBML header's `DocTypeVersion` at least 2: a stored lower value is raised, and an empty or missing one is written (RFC 9559 section 7); no Segment Position moves. A track the caller did not set keeps its bytes.
- `AiffFile.Id3v2Tag`, and `AiffFile.Title`, `Artist`, `Comment` and `Copyright`

### Changed

- Matroska: `TotalTracks` and `TotalDiscs` follow the tagging spec. `TOTAL_PARTS` counts the parts of the first lower level ([tagging](https://www.matroska.org/technical/tagging.html)), so at 50 it is the album's track count and at 60 the set's disc count ([audio examples](https://www.matroska.org/technical/tagging-audio-example.html), "Collection of CD sets"). `Track` is `PART_NUMBER` at 30 and `DiscNumber` is `PART_NUMBER` at 50. ffmpeg's untargeted `PART_NUMBER` (read as the track), its `4/12` and `DISC=2/3` slash forms and its `TRACKTOTAL`/`DISCTOTAL` names are read as fallbacks; setting a total writes `TOTAL_PARTS` and removes them. A `TOTAL_PARTS` at 30 is read as the track total only when nothing else gives one, and is never written.
- Matroska: TagLib writes the track total at 30 and the disc total at 50, against the spec. A TagLib-written file holding both totals now reads its disc total as `TotalTracks`, and TagLib reads a stored level 50 `TOTAL_PARTS` written here as its disc total, reads an untargeted one (what this library writes on ffmpeg files) as its track total, and does not read 60. This is a deliberate spec choice.
- Matroska: a new 60 or 50 tag goes before the file's narrower tags and before ffmpeg's untargeted tag, the order of the spec's examples, so readers that keep the last value they meet (MediaInfo) still show the track. New fields go into ffmpeg's untargeted tag, not into a disc-only tag. Setting `TotalDiscs` on a file with no 40 or 50 tag first moves a known disc number to 50.

### Fixed

- Ogg Vorbis comment packets that run across pages without the continuation flag keep their comments, and a stream whose last audio page is cut off keeps its duration (#126). `missing_flag.ogg` from that issue now opens with its title, artist, album, genre, cover and 10.189 s duration. Its framing byte is 0x01: the comments were lost in page reassembly, not at the framing bit.
- The Vorbis comment framing bit is read as the spec defines it: the low bit of the first byte after the declared comment list ([v-comment.html](https://xiph.org/vorbis/doc/v-comment.html) steps 7-8, Vorbis I spec 5.2.1, bit order per 2.1.4). 0x01, 0x03, 0x81 and 0xFF are accepted, and padding after that byte is ignored. A framing byte of 0x00, any other byte with the low bit clear, or a packet that ends at the comment list is rejected, as in libvorbis and mutagen. All 60 Ogg Vorbis files in the test corpora use 0x01.
- Ogg Opus: OpusTags split across pages without the continuation flag keeps its tags (they were empty). When the last audio page is cut off, `MediaFile.Read` on a stream reports the duration instead of 0, for example `psycho4.opus` (244 s).
- Matroska: a track with `FlagEnabled` 0 is no longer chosen as the main audio or video track while an enabled track of that type exists (#117).
- AIFF and AIFF-C files return their native `ANNO`, `COMT` (marker id 0), `NAME`, `AUTH` and `(c) ` text through `Tag` when the ID3 tag has no value for that field (#127). Text is read as Mac OS Roman, or as UTF-8 when the whole value is valid UTF-8 with a multibyte sequence
- Editing Title, Artist, Comment, Copyright or Performers on an AIFF file writes the value to the ID3 chunk and removes that field's native chunk on save, even when the new value equals the old one. `COMT` notes attached to other markers are kept byte for byte. An unchanged save leaves the native chunks as they are
- Matroska: when a save inserts tags before an unknown-size Cluster, each Cluster `Position` at or past the insertion point moves with the data and the Cluster CRC-32 is recomputed (#118). `CueCodecState`, a Segment Position (RFC 9559 section 5.1.5.1.2.6), moves the same way, and is voided if the new value does not fit. A live `0` stays `0`, and a `Position` that no longer fits is voided, since it is optional. The walk is linear in the number of top-level elements.
- Matroska: `DiscNumber` and `TotalDiscs` are read and written (#130). Setting or clearing a track or disc number or total leaves the other three as they were, including a total held in a `4/12` or `2/3` slash, and a clear removes every element the getter could still read.
- Matroska: every rendered Tag has a `Targets` element. An omitted `TargetTypeValue` is not written back as 50, and an empty one reads as the default 50. A Tag that a clear leaves with no SimpleTag is not written (RFC 9559 section 5.1.8.1.2); one that was already empty when read is kept.

### Changed

- An APE artist, album artist, composer, or genre is the first name. It is not a comma-joined string. A Basic copy from APE into MP4, ASF, RIFF INFO, or ID3v1 keeps that first name. A copy into ID3v2 keeps the full list.
- A single-value ID3v2 or Vorbis Comment property, and an APE artist, album artist, composer, or genre, returns the first non-empty value, and an empty string only when every value is empty. Other APE text items still read every non-empty value joined with ", " (`A\0B` reads "A, B"). So do the public `Id3v2Tag.GetTextFrame`, `Id3v2Tag.GetUserText`, and `VorbisComment.GetValue`, and the ID3v2 comment and lyrics, which read the first non-empty frame without a description, then the first other one. ID3v2.4 §4.2 stores several strings as a NUL-separated list and Vorbis field names may repeat, so `"\0V2"` and `ARTIST=` + `ARTIST=V2` read `V2`. A value is kept as written: only an empty value is skipped, so `" \0B"` reads `" "`, as other readers show it. `Id3v2Tag.GetTextFrameValues` and the ID3v2 list properties no longer trim or drop blank NUL-separated values, so for NUL-separated values the single value and the first list entry agree. A value with `/` and no NUL is still split on `/` and trimmed, in any ID3v2 version, so `AC/DC` reads Artist `AC/DC` and Performers [`AC`, `DC`], as before. This replaces the earlier reading of a leading NUL as an empty first value, and it differs from mutagen and TagLib#, which return the empty first ID3v2 value; for a comment it matches what ffprobe and TagLib# show. Writing back the value read changes nothing, so a multi-value frame, item, or repeated field keeps every value. `= ""` clears: it removes the ID3v2 frame (for a comment or lyrics, every frame without a description) or the APE item, and leaves one empty Vorbis field. The comparison is ordinal, so a case-only edit still writes, and `null` clears like `= ""`. For an ID3v2 comment or lyrics, both remove only the frames without a description; a described comment or lyrics frame (such as `ID3v1 Comment`) is then still read, as before. This covers every ID3v2 string property (every text and TXXX frame, the year, the podcast feed URL, the MusicBrainz recording ID, the comment and the lyrics, and the movement number and total), every APE text property, and every Vorbis text field.
- `CopyTo` reads every source value before it writes any. It replaces the target's list with the source value, even when that equals the target's first value. A list (performers, album artists, composers, genres, and the sort and role lists) that the target already holds is left as it is, and with `Extended` the artist and genre travel in those lists, so a `Basic` copy alone is what writes a single name over a list. When the source and target read from a common tag (a tag copied into itself, or a file's combined `Tag` copied into one of its own tags or into another view over them, as two reads of `Mp3File.Tag` are), the copy only adds values the target does not show. An empty source value still clears the field, as `= ""` does, so through a view it also clears what the view showed from another tag (an ID3v1 title, an AIFF `NAME` chunk). So `mp3.Id3v2Tag.CopyTo (mp3.Tag)` no longer rewrites the ID3v1 tag with values the combined view already shows from ID3v2. A fallback field of its own beside a value equal to the copied one stays: Vorbis `ORIGINALYEAR`, APE `Date`, and the ID3v2.3 `TDOR` beside `TORY`. Pictures are outside these rules: a non-empty source picture list always replaces the target's, as before.
- A list copied into a tag that holds one name (MP4, ASF, RIFF INFO, ID3v1, or the base `Tag` list setters) stores its first non-empty entry, not an empty first entry. A copy of `ARTIST=` + `ARTIST=V2` into an MP4, WAV, or WMA file stores `V2`, as a `Basic` copy already did, instead of clearing the artist and genre.
- Vorbis and APE movements follow MusicBrainz Picard's tag mapping: the name is `MOVEMENTNAME` and the number is `MOVEMENT` (`MOVEMENTNUMBER` is still read first and kept in step). A new name is written to `MOVEMENTNAME` and a new number to `MOVEMENT`, where the name went to `MOVEMENT` and the number to `MOVEMENTNUMBER` before. A `MOVEMENT` that is not a number is still read as the name when there is no `MOVEMENTNAME`, and is kept in step with a new name; a numeric name then goes to `MOVEMENTNAME` only. Clearing one half never changes how the other half reads, and a copy from a Picard FLAC no longer writes the number "2" over an ID3v2 `MVNM`.
- Clearing an APE year also removes `Date`, and setting a new year also writes it to an existing `Date`. A locator reads as the same string a title locator reads. An empty text item reads as an empty string, and a binary item reads as null, for a name and for a title.
- Clearing a Vorbis original release date also removes `ORIGINALYEAR`, and setting a new date rewrites an existing `ORIGINALYEAR` to its four-digit year. Writing back the APE year, Vorbis original release date, or movement already read changes nothing, so a Picard or Mp3tag tag keeps every field. An empty `Year` or `ORIGINALDATE` no longer hides a filled `Date` or `ORIGINALYEAR`; the same holds for an empty ID3v2 `MVNM`, and for a `TDOR` or `TORY` that holds no date.
- An ID3v2 movement write now also stores the name in Apple's `MVNM` frame, and a movement number or total also updates `MVIN`, keeping the other half. Clearing removes those frames too.
- Setting a new ID3v2 original release date, or clearing it, removes both `TDOR` and `TORY`, then writes the frame for the tag's version, so the other version's frame cannot come back. Writing back the date already read changes nothing.

## [0.6.0] - 2026-01-05

### Breaking Changes

- **Removed**: `MusicIpId` property from `Tag` base class (MusicIP service discontinued, use AcoustID instead)
- **Removed**: `AiffFile.TryParse` method (use `AiffFile.Read` which returns `AiffFileReadResult` with error context)
- **Removed**: `WavFile.ReadFromData` method (use `WavFile.Read` for span-based parsing)
- **Changed**: `AudioProperties` converted from class to readonly record struct for better performance and immutability
- **Renamed**: `AsfFile.AudioProperties` property renamed to `Properties` for consistency with other file types

### Added

- Comprehensive test suite expanded to 3,780 tests (from 3,175)
- Test coverage increased to 90.1% line coverage, 77.6% branch coverage
- Additional MP3 APE tag test coverage for edge cases

### Changed

- Performance improvements from AudioProperties struct conversion
- Improved API consistency across file format implementations
- Enhanced documentation and code examples

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
- ~~MusicIpId~~ - Removed (MusicIP service discontinued, use AcoustID instead)
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

[0.6.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.6.0
[0.5.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.5.0
[0.4.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.4.0
[0.3.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.3.0
[0.2.1]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.2.1
[0.2.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.2.0
[0.1.0]: https://github.com/decriptor/TagLibSharp2/releases/tag/v0.1.0
