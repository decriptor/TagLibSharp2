# MP4/M4A Acceptance Criteria

**Version:** 1.0
**Date:** 2025-12-31
**Target Release:** TagLibSharp2 v0.4.0
**Feature:** MP4/M4A Audio File Format Support

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [User Stories](#user-stories)
3. [Acceptance Criteria](#acceptance-criteria)
4. [Definition of Done](#definition-of-done)
5. [Out of Scope](#out-of-scope)
6. [Success Metrics](#success-metrics)
7. [Test Coverage Requirements](#test-coverage-requirements)

---

## Executive Summary

MP4/M4A format support is the most requested feature for TagLibSharp2 and is critical for Apple ecosystem users. This feature enables developers to read and write metadata for AAC and ALAC audio files using the iTunes-style metadata format.

**Primary Use Cases:**
- Music library organization and management
- Podcast episode metadata editing
- Audiobook chapter management
- Album artwork management
- Cross-platform audio metadata editing

**Target Formats:**
- M4A (AAC-LC compressed audio)
- M4A (ALAC lossless audio)
- M4B (audiobook with chapters)

---

## User Stories

### US-1: Read MP4 Metadata

**As a** developer building a music library application
**I want to** read MP4/M4A metadata using TagLibSharp2
**So that I can** display song information, album artwork, and organize music files

**Acceptance Criteria:**
- Read all P0 metadata atoms (title, artist, album, etc.)
- Extract cover art in JPEG and PNG formats
- Read track and disc numbers with total counts
- Read boolean flags (compilation, gapless playback)
- Read tempo/BPM values
- Handle files with missing or partial metadata gracefully

---

### US-2: Write MP4 Metadata

**As a** developer building a metadata editor
**I want to** write MP4/M4A metadata using TagLibSharp2
**So that I can** organize music libraries and correct incorrect metadata

**Acceptance Criteria:**
- Write all P0 metadata atoms
- Embed cover art (JPEG and PNG)
- Set track and disc numbers
- Set compilation and gapless playback flags
- Preserve unknown atoms during write
- Maintain file playability after write
- Support atomic file writes (no corruption on failure)

---

### US-3: Read Cover Art from M4A Files

**As a** developer building a music player UI
**I want to** extract album artwork from M4A files
**So that I can** display cover art in the player interface

**Acceptance Criteria:**
- Extract JPEG cover art (type indicator 13)
- Extract PNG cover art (type indicator 14)
- Support multiple embedded images
- Provide image format detection
- Return image binary data for display
- Handle files with no artwork gracefully

---

### US-4: Detect AAC vs ALAC Codec

**As a** developer building a codec analyzer
**I want to** detect the audio codec in M4A files
**So that I can** show users whether files are lossy or lossless

**Acceptance Criteria:**
- Identify AAC-LC codec from esds box
- Identify ALAC codec from alac magic cookie
- Distinguish AAC variants (LC, Main, SSR, LTP)
- Return codec name as string
- Detect other audio codecs (AC-3, Opus, MP3)
- Handle multi-codec files (return primary audio track codec)

---

### US-5: Extract Accurate Audio Duration

**As a** developer building a playlist manager
**I want to** get accurate playback duration for M4A files
**So that I can** show total playlist time and individual track lengths

**Acceptance Criteria:**
- Extract duration from mvhd (movie header)
- Extract duration from mdhd (media header)
- Calculate duration in seconds with millisecond precision
- Handle edit lists for gapless playback
- Match ffprobe duration within 1 second
- Support files with unusual timescales

---

### US-6: Extract Audio Properties

**As a** developer building an audio file analyzer
**I want to** read sample rate, bit depth, channels, and bitrate
**So that I can** show detailed technical information about audio files

**Acceptance Criteria:**
- Extract sample rate for AAC (from esds Audio Specific Config)
- Extract sample rate for ALAC (from magic cookie)
- Extract channel count (mono, stereo, 5.1, 7.1)
- Extract bit depth for ALAC (16, 20, 24, 32-bit)
- Calculate or extract bitrate (CBR and VBR)
- Handle unusual sample rates (8kHz to 96kHz)

---

### US-7: Round-Trip Metadata Preservation

**As a** developer building a metadata editor
**I want to** read, modify, and write M4A files without data loss
**So that I can** safely edit metadata without corrupting files or losing information

**Acceptance Criteria:**
- Preserve all unmodified metadata atoms
- Preserve audio data exactly (byte-for-byte)
- Maintain file structure integrity
- Keep unknown atoms unchanged
- Preserve edit lists and other technical atoms
- No data loss on consecutive read-write cycles

---

### US-8: Handle Large Files

**As a** developer building an audiobook editor
**I want to** work with large M4B files (>4GB)
**So that I can** edit long-form audiobook metadata

**Acceptance Criteria:**
- Support files larger than 4GB
- Use 64-bit chunk offsets (co64) when present
- Use 64-bit durations (version 1 boxes)
- Handle largesize fields (size == 1)
- Stream file reading without loading entire file into memory

---

## Acceptance Criteria

### Feature: Read MP4 Metadata

#### Scenario: Read title from M4A file
```gherkin
Given an M4A file with title "Test Song"
When I read the file with TagLibSharp2
Then the Tag.Title property should be "Test Song"
```

#### Scenario: Read artist from M4A file
```gherkin
Given an M4A file with artist "Test Artist"
When I read the file with TagLibSharp2
Then the Tag.Artist property should be "Test Artist"
```

#### Scenario: Read album from M4A file
```gherkin
Given an M4A file with album "Test Album"
When I read the file with TagLibSharp2
Then the Tag.Album property should be "Test Album"
```

#### Scenario: Read album artist from M4A file
```gherkin
Given an M4A file with album artist "Various Artists"
When I read the file with TagLibSharp2
Then the Tag.AlbumArtist property should be "Various Artists"
```

#### Scenario: Read track number with total
```gherkin
Given an M4A file with track 3 of 12
When I read the file with TagLibSharp2
Then the Tag.Track property should be 3
And the Tag.TrackCount property should be 12
```

#### Scenario: Read disc number with total
```gherkin
Given an M4A file with disc 2 of 3
When I read the file with TagLibSharp2
Then the Tag.Disc property should be 2
And the Tag.DiscCount property should be 3
```

#### Scenario: Read genre from M4A file
```gherkin
Given an M4A file with genre "Jazz"
When I read the file with TagLibSharp2
Then the Tag.Genre property should be "Jazz"
```

#### Scenario: Read year from M4A file
```gherkin
Given an M4A file with year "2025"
When I read the file with TagLibSharp2
Then the Tag.Year property should be 2025
```

#### Scenario: Read comment from M4A file
```gherkin
Given an M4A file with comment "Test comment"
When I read the file with TagLibSharp2
Then the Tag.Comment property should be "Test comment"
```

#### Scenario: Read tempo/BPM from M4A file
```gherkin
Given an M4A file with BPM 120
When I read the file with TagLibSharp2
Then the Tag.BeatsPerMinute property should be 120
```

#### Scenario: Read compilation flag
```gherkin
Given an M4A file marked as compilation
When I read the file with TagLibSharp2
Then the Tag.IsCompilation property should be true
```

#### Scenario: Read missing metadata gracefully
```gherkin
Given an M4A file with no metadata
When I read the file with TagLibSharp2
Then the Tag.Title property should be null
And no exception should be thrown
```

---

### Feature: Write MP4 Metadata

#### Scenario: Write title to M4A file
```gherkin
Given an M4A file
When I set Tag.Title to "New Title"
And I save the file
Then reading the file again should show Tag.Title as "New Title"
```

#### Scenario: Write artist to M4A file
```gherkin
Given an M4A file
When I set Tag.Artist to "New Artist"
And I save the file
Then reading the file again should show Tag.Artist as "New Artist"
```

#### Scenario: Write track number
```gherkin
Given an M4A file
When I set Tag.Track to 5
And I set Tag.TrackCount to 10
And I save the file
Then reading the file again should show track 5 of 10
```

#### Scenario: Update existing metadata
```gherkin
Given an M4A file with title "Old Title"
When I set Tag.Title to "New Title"
And I save the file
Then reading the file again should show Tag.Title as "New Title"
And the file should remain playable
```

#### Scenario: Remove metadata
```gherkin
Given an M4A file with title "Test"
When I set Tag.Title to null
And I save the file
Then reading the file again should show Tag.Title as null
```

#### Scenario: Preserve unmodified metadata
```gherkin
Given an M4A file with title "Song" and artist "Artist"
When I set Tag.Album to "Album"
And I save the file
Then reading the file again should show all three fields correctly
```

---

### Feature: Cover Art Support

#### Scenario: Read JPEG cover art
```gherkin
Given an M4A file with JPEG album artwork
When I read the file with TagLibSharp2
Then the Tag.Pictures array should contain 1 picture
And the picture format should be JPEG
And the picture data should match the embedded image
```

#### Scenario: Read PNG cover art
```gherkin
Given an M4A file with PNG album artwork
When I read the file with TagLibSharp2
Then the Tag.Pictures array should contain 1 picture
And the picture format should be PNG
And the picture data should match the embedded image
```

#### Scenario: Write JPEG cover art
```gherkin
Given an M4A file
When I add a JPEG image to Tag.Pictures
And I save the file
Then reading the file again should contain the JPEG image
And the file should be playable in iTunes
```

#### Scenario: Write PNG cover art
```gherkin
Given an M4A file
When I add a PNG image to Tag.Pictures
And I save the file
Then reading the file again should contain the PNG image
And the file should be playable in iTunes
```

#### Scenario: Replace cover art
```gherkin
Given an M4A file with existing cover art
When I replace Tag.Pictures with a new image
And I save the file
Then reading the file again should contain only the new image
```

#### Scenario: Remove cover art
```gherkin
Given an M4A file with cover art
When I clear Tag.Pictures
And I save the file
Then reading the file again should have no pictures
```

---

### Feature: Codec Detection

#### Scenario: Detect AAC-LC codec
```gherkin
Given an M4A file with AAC-LC audio
When I read the file with TagLibSharp2
Then the AudioProperties.Codec should be "AAC-LC"
```

#### Scenario: Detect ALAC codec
```gherkin
Given an M4A file with ALAC audio
When I read the file with TagLibSharp2
Then the AudioProperties.Codec should be "ALAC"
```

#### Scenario: Detect sample entry type
```gherkin
Given an M4A file
When I read the file with TagLibSharp2
Then the sample entry type should be "mp4a" or "alac"
```

---

### Feature: Audio Properties

#### Scenario: Extract duration with millisecond precision
```gherkin
Given an M4A file with 3 minutes 45.678 seconds duration
When I read the file with TagLibSharp2
Then the AudioProperties.Duration should be within 0.1 seconds of actual
```

#### Scenario: Extract sample rate for AAC
```gherkin
Given an AAC M4A file with 44100 Hz sample rate
When I read the file with TagLibSharp2
Then the AudioProperties.SampleRate should be 44100
```

#### Scenario: Extract sample rate for ALAC
```gherkin
Given an ALAC M4A file with 48000 Hz sample rate
When I read the file with TagLibSharp2
Then the AudioProperties.SampleRate should be 48000
```

#### Scenario: Extract channel count
```gherkin
Given an M4A file with stereo audio
When I read the file with TagLibSharp2
Then the AudioProperties.Channels should be 2
```

#### Scenario: Extract bit depth for ALAC
```gherkin
Given an ALAC M4A file with 24-bit audio
When I read the file with TagLibSharp2
Then the AudioProperties.BitsPerSample should be 24
```

#### Scenario: Extract bitrate for AAC
```gherkin
Given an AAC M4A file with 256 kbps bitrate
When I read the file with TagLibSharp2
Then the AudioProperties.Bitrate should be within 10 kbps of 256
```

#### Scenario: Calculate bitrate for VBR files
```gherkin
Given an AAC M4A file with variable bitrate
When I read the file with TagLibSharp2
Then the AudioProperties.Bitrate should reflect the average bitrate
```

---

### Feature: Round-Trip Integrity

#### Scenario: Preserve all metadata on round-trip
```gherkin
Given an M4A file with complete metadata
When I read the file
And I modify Tag.Title
And I save the file
And I read the file again
Then all other metadata fields should be unchanged
```

#### Scenario: Preserve audio data exactly
```gherkin
Given an M4A file
When I modify metadata and save
Then the audio data should be byte-identical to the original
```

#### Scenario: Preserve unknown atoms
```gherkin
Given an M4A file with custom vendor atoms
When I modify metadata and save
Then the custom atoms should be preserved
```

#### Scenario: Consecutive writes
```gherkin
Given an M4A file
When I perform 5 consecutive read-modify-write cycles
Then the file should remain valid and playable
And no metadata should be lost
```

---

### Feature: Large File Support

#### Scenario: Handle 5GB M4B file
```gherkin
Given an M4B audiobook file larger than 4GB
When I read the file with TagLibSharp2
Then metadata should be read successfully
And memory usage should be reasonable (<100MB)
```

#### Scenario: Use 64-bit chunk offsets
```gherkin
Given an M4A file with co64 (64-bit chunk offset) atom
When I read the file with TagLibSharp2
Then the file should be parsed correctly
```

---

### Feature: Error Handling

#### Scenario: Handle corrupted ftyp
```gherkin
Given an M4A file with corrupted file type box
When I attempt to read the file
Then a descriptive error should be returned
And no exception should crash the application
```

#### Scenario: Handle truncated file
```gherkin
Given an M4A file that was truncated during download
When I attempt to read the file
Then available metadata should be extracted
And an error should indicate the file is incomplete
```

#### Scenario: Handle DRM-protected file
```gherkin
Given an M4A file with FairPlay DRM
When I attempt to read the file
Then an error should indicate the file is protected
And no attempt to decrypt should be made
```

---

## Definition of Done

### P0 Metadata Atoms (Must Support)

- [ ] `©nam` - Title
- [ ] `©ART` - Artist
- [ ] `©alb` - Album
- [ ] `aART` - Album Artist
- [ ] `trkn` - Track Number (with total)
- [ ] `disk` - Disc Number (with total)
- [ ] `covr` - Cover Art (JPEG and PNG)
- [ ] `©gen` - Genre
- [ ] `©day` - Year/Date
- [ ] `©cmt` - Comment
- [ ] `©lyr` - Lyrics
- [ ] `©wrt` - Composer
- [ ] `©grp` - Grouping
- [ ] `cpil` - Compilation flag
- [ ] `pgap` - Gapless playback flag
- [ ] `tmpo` - BPM/Tempo

### Audio Properties (Must Extract)

- [ ] AAC codec detection and identification
- [ ] ALAC codec detection and identification
- [ ] Duration accurate to within 1 second of ffprobe
- [ ] Sample rate extraction (AAC and ALAC)
- [ ] Channel count extraction
- [ ] Bit depth extraction (ALAC)
- [ ] Bitrate extraction or calculation

### File Operations (Must Work)

- [ ] Read metadata from M4A files
- [ ] Write metadata to M4A files
- [ ] Round-trip preserves all metadata
- [ ] Atomic file writes (no corruption on failure)
- [ ] Files remain playable after write

### Compatibility (Must Pass)

- [ ] Files written are playable in iTunes/Music.app
- [ ] Files written are playable in VLC
- [ ] Files written are readable by foobar2000
- [ ] Files written are readable by Mp3tag
- [ ] Files written are readable by MusicBrainz Picard

### Code Quality (Must Meet)

- [ ] 100+ unit tests covering core functionality
- [ ] Integration tests with real M4A/M4B files
- [ ] Round-trip tests (read → write → read)
- [ ] XML documentation on all public APIs
- [ ] Clean room implementation (no GPL/LGPL code)

### Integration (Must Complete)

- [ ] Integration with `MediaFile.Open()` factory
- [ ] Integration with `MediaFormat` enum
- [ ] Support for `.m4a`, `.m4b`, `.mp4` (audio) extensions
- [ ] Result type pattern (no exceptions for expected errors)

---

## Out of Scope

The following features are explicitly **NOT** included in v0.4.0:

### Video Track Support
- Video codec detection (avc1, hvc1, etc.)
- Video media header (vmhd) parsing
- Video frame analysis
- Video thumbnail extraction

**Rationale:** TagLibSharp2 is an audio metadata library. Video support adds significant complexity for limited benefit.

### DRM Decryption
- FairPlay DRM decryption
- CENC/Widevine decryption
- Protected content playback

**Rationale:** DRM decryption is legally complex and out of scope for a metadata library. Files will be detected as protected and reading will fail gracefully.

### Movie Fragments (DASH/HLS)
- Movie fragment (moof) boxes
- Track fragment (traf) boxes
- Segment index (sidx) boxes
- Fragmented file support

**Rationale:** Streaming formats use different structures. Initial release focuses on traditional M4A files.

### Chapter Support (Deferred to v0.4.1)
- Chapter list (chpl) atom
- Chapter markers for audiobooks
- Timed text tracks

**Rationale:** Chapter support is important but can be added in a point release after core functionality is stable.

### Advanced Metadata
- TV show metadata (tvsh, tven, tvnn)
- Podcast-specific atoms (purl, egid)
- iTunes Store IDs (cnID, atID, plID)
- Movement/work metadata (classical music)

**Rationale:** These are P1/P2 features that can be added after core tagging works.

### File Optimization
- Relocating moov atom to beginning of file
- Padding management
- File size optimization

**Rationale:** Optimization features can be added later. Initial focus is correctness.

### Multi-Track Files
- Multiple audio track support
- Language-specific track selection
- Track enable/disable flags

**Rationale:** Most M4A files have single audio track. Multi-track support can be added in v0.5.0.

---

## Success Metrics

### Zero Data Loss
- **Target:** 100% metadata preservation on round-trip
- **Measurement:** Automated round-trip tests with 100+ real-world files
- **Pass Criteria:** All fields match after read → write → read cycle

### Performance
- **Target:** Read time <10ms for typical M4A file
- **Measurement:** Benchmark suite with files of varying sizes
- **Pass Criteria:** 95th percentile read time under 10ms on reference hardware

### Compatibility
- **Target:** Files playable in all major players
- **Measurement:** Manual testing with iTunes, VLC, foobar2000
- **Pass Criteria:** Zero playback errors across all tested players

### Code Coverage
- **Target:** 90%+ code coverage for MP4 module
- **Measurement:** Code coverage analysis with dotnet test
- **Pass Criteria:** All public APIs covered, all critical paths tested

### Duration Accuracy
- **Target:** Duration matches ffprobe within 1 second
- **Measurement:** Compare AudioProperties.Duration with ffprobe output
- **Pass Criteria:** 100% of test files within tolerance

---

## Test Coverage Requirements

### Unit Tests (Minimum 100 tests)

#### Box Parsing Tests (20 tests)
- ftyp box parsing (valid brands)
- mvhd box parsing (version 0 and 1)
- mdhd box parsing (version 0 and 1)
- tkhd box parsing
- hdlr box parsing (audio handler detection)
- stsd box parsing (sample descriptions)
- esds box parsing (AAC config)
- alac magic cookie parsing
- ilst box parsing (metadata item list)
- data atom parsing (UTF-8, integer, JPEG, PNG)

#### Metadata Tests (30 tests)
- Read/write each P0 text field (10 tests)
- Read/write track/disc numbers (4 tests)
- Read/write boolean flags (4 tests)
- Read/write BPM (2 tests)
- Read/write cover art JPEG (3 tests)
- Read/write cover art PNG (3 tests)
- Multiple cover art images (2 tests)
- Empty/null metadata handling (2 tests)

#### Audio Properties Tests (20 tests)
- Duration extraction (mvhd)
- Duration extraction (mdhd)
- Sample rate extraction (AAC from esds)
- Sample rate extraction (ALAC from magic cookie)
- Channel count extraction
- Bit depth extraction (ALAC)
- Bitrate extraction (AAC from esds)
- Bitrate calculation (from sample table)
- Codec detection (AAC-LC, ALAC)
- Edge cases (unusual sample rates, high bit depths)

#### Round-Trip Tests (15 tests)
- Read → write → read (metadata preserved)
- Modify one field (others unchanged)
- Add new metadata (existing preserved)
- Remove metadata (file valid)
- Multiple consecutive writes
- Large file (>100MB) round-trip

#### Error Handling Tests (15 tests)
- Corrupted ftyp box
- Missing required boxes (moov, mdhd)
- Invalid box sizes
- Truncated file
- DRM-protected file detection
- Malformed metadata atoms
- Invalid data type indicators
- Zero-byte file
- Non-MP4 file

### Integration Tests (20 tests)

#### Real-World Files
- AAC M4A from iTunes Store
- ALAC M4A from Apple Music
- M4B audiobook with chapters
- M4A with large cover art (>2MB)
- M4A with no metadata
- M4A with complete metadata
- Large file (>1GB)
- Multi-track file

#### Cross-Tool Compatibility
- Read file created by iTunes
- Read file created by foobar2000
- Write file readable by iTunes
- Write file readable by VLC
- Write file readable by Mp3tag

---

## Acceptance Sign-Off

### Stakeholder Approval

- [ ] Product Manager - Feature complete
- [ ] Lead Developer - Code review passed
- [ ] QA - All tests passing
- [ ] Technical Writer - Documentation complete

### Release Readiness

- [ ] All P0 acceptance criteria met
- [ ] Definition of Done checklist complete
- [ ] Success metrics targets achieved
- [ ] Test coverage requirements met
- [ ] No P0 or P1 bugs outstanding
- [ ] Performance benchmarks passed
- [ ] Compatibility testing complete
- [ ] Documentation published

---

**Document Status:** Draft
**Last Updated:** 2025-12-31
**Next Review:** Upon implementation start
