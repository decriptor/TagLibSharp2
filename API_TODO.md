# TagLibSharp2 API TODO

> Generated from expert reviews on 2026-01-04

## Critical (Before v1.0)

### 1. Add `IMediaFile` Interface ✅
- [x] Create `IMediaFile` interface with common operations
- [x] All file classes implement `IMediaFile`
- [x] Change `MediaFileResult.File` from `object?` to `IMediaFile?`

**Why:** Currently `MediaFileResult.File` returns `object?`, forcing consumers to cast. No way to write polymorphic code.

### 2. Standardize Method Naming ✅
- [x] Rename `MediaFile.Open()` to `MediaFile.Read()`
- [x] Rename `MediaFile.OpenAsync()` to `MediaFile.ReadAsync()`
- [x] Rename `MediaFile.OpenFromData()` to `MediaFile.ReadFromData()`
- [x] Update all documentation and examples

**Why:** Inconsistent with file class pattern (`FlacFile.Read()`, `Mp3File.Read()`).

### 3. Make FileReadResult.Data Immutable ✅
- [x] Change `FileReadResult.Data` from `byte[]?` to `ReadOnlyMemory<byte>?`
- [x] Update all consumers

**Why:** Result types should be immutable. Mutable array violates this principle.

### 4. Add Round-Trip Tests (12 formats)
- [x] FlacFileRoundTripTests.cs ✅
- [x] Mp3FileRoundTripTests.cs ✅
- [x] WavFileRoundTripTests.cs ✅
- [x] AiffFileRoundTripTests.cs ✅
- [x] OggVorbisFileRoundTripTests.cs ✅
- [x] OggOpusFileRoundTripTests.cs ✅
- [x] OggFlacFileRoundTripTests.cs ✅
- [x] AsfFileRoundTripTests.cs ✅
- [x] DsfFileRoundTripTests.cs ✅
- [x] DffFileRoundTripTests.cs ✅
- [x] WavPackFileRoundTripTests.cs ✅
- [x] MonkeysAudioFileRoundTripTests.cs ✅
- [x] MusepackFileRoundTripTests.cs ✅

**Why:** Mp4, FLAC, MP3, WAV, AIFF have dedicated round-trip tests. Remaining formats need coverage.

## High Priority

### 5. Add Async Tests (13 formats)
- [ ] FlacFileAsyncTests.cs
- [ ] Mp3FileAsyncTests.cs
- [ ] WavFileAsyncTests.cs
- [ ] AiffFileAsyncTests.cs
- [ ] OggVorbisFileAsyncTests.cs
- [ ] OggOpusFileAsyncTests.cs
- [ ] OggFlacFileAsyncTests.cs
- [ ] AsfFileAsyncTests.cs
- [ ] DsfFileAsyncTests.cs
- [ ] DffFileAsyncTests.cs
- [ ] WavPackFileAsyncTests.cs
- [ ] MonkeysAudioFileAsyncTests.cs
- [ ] MusepackFileAsyncTests.cs

**Why:** Async methods exist but lack comprehensive tests.

### 6. Add Tag.Clear() Tests
- [ ] Id3v1Tag.Clear() test
- [ ] Id3v2Tag.Clear() test
- [ ] VorbisComment.Clear() test
- [ ] ApeTag.Clear() test
- [ ] Mp4Tag.Clear() dedicated test
- [ ] BextTag.Clear() test (if exists)

**Why:** Clear() method untested for most tag types.

### 7. Document Save Workflow
- [ ] Add comprehensive examples to MediaFile.cs
- [ ] Document `originalData` parameter purpose
- [ ] Add read-modify-save workflow examples
- [ ] Document which overload to use when

**Why:** Save operations are confusing without examples.

### 8. Add DISCTOTAL Fallback in VorbisComment
- [ ] Read from DISCTOTAL field as fallback for TotalDiscs
- [ ] Write tests first

**Why:** Vorbis spec uses DISCTOTAL, some software uses TOTALDISCS.

## Medium Priority

### 9. Add IsValidFormat() Static Methods
- [ ] Mp3File.IsValidFormat(ReadOnlySpan<byte>)
- [ ] FlacFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] Mp4File.IsValidFormat(ReadOnlySpan<byte>)
- [ ] OggVorbisFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] OggOpusFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] OggFlacFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] WavFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] AiffFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] AsfFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] DsfFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] DffFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] WavPackFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] MonkeysAudioFile.IsValidFormat(ReadOnlySpan<byte>)
- [ ] MusepackFile.IsValidFormat(ReadOnlySpan<byte>)

**Why:** Users want to check format before parsing.

### 10. Standardize Property Names
- [ ] Audit all file classes for TrackCount vs TotalTracks
- [ ] Audit all file classes for DiscCount vs TotalDiscs
- [ ] Ensure consistent naming with Tag base class

**Why:** Some files use TrackCount, Tag base uses TotalTracks.

### 11. Add DISCSUBTITLE Property
- [ ] Add to Tag base class
- [ ] Implement in VorbisComment (DISCSUBTITLE field)
- [ ] Implement in ApeTag (DISCSUBTITLE item)
- [ ] Implement in Id3v2Tag (TSST frame)
- [ ] Implement in Mp4Tag (if applicable)

**Why:** Used for multi-disc albums with different disc titles.

### 12. Document Format Limitations
- [ ] ID3v1: 30-byte field limits, Latin-1 encoding
- [ ] ID3v1: Track number 0-255 only
- [ ] ID3v1: Single genre only
- [ ] MP4: Track/disc numbers limited to 65535
- [ ] Add `<remarks>` sections to Tag.cs properties

**Why:** Users don't know about silent truncation.

## Low Priority

### 13. Add APE Tag ALBUMARTIST Fallback
- [ ] Read from "ALBUMARTIST" (no space) as fallback
- [ ] Prefer "Album Artist" (with space) per spec

**Why:** Some taggers use no-space variant.

### 14. Expose FLAC StreamInfo Details
- [ ] Create FlacStreamInfo class
- [ ] Expose min/max block size
- [ ] Expose min/max frame size
- [ ] Expose MD5 signature

**Why:** Available in STREAMINFO but not exposed.

### 15. Add MP4 Podcast Atoms
- [ ] egid (episode GUID)
- [ ] pcst (podcast flag)
- [ ] catg (category)
- [ ] keyw (keywords)

**Why:** Incomplete podcast metadata support.

### 16. Add Missing MP4 Atoms
- [ ] ©gen (custom genre)
- [ ] rtng (content rating: Clean/Explicit)

**Why:** Used by iTunes but not exposed.

## Documentation Only

### 17. Add XML Doc Examples
- [ ] MediaFile usage examples
- [ ] Frame management examples (Id3v2Tag)
- [ ] Error handling patterns
- [ ] CopyTo usage examples

### 18. Document Intentional Design Decisions
- [ ] MusicBrainzRecordingId = MusicBrainzTrackId (historical)
- [ ] Array properties for TagLib# compatibility
- [ ] uint for track numbers (non-negative semantics)

---

## Review Grades

| Reviewer | Grade | Notes |
|----------|-------|-------|
| C# API Design | B+ | Naming consistency issues |
| SDET Coverage | B | Missing async/round-trip tests |
| Audio Format | A- | Technically correct, minor gaps |
| Documentation | B- | Missing workflow examples |
| Breaking Change Risk | 7/10 | Must add IMediaFile before v1.0 |
