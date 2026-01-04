# TagLibSharp2 Milestone Map

*Synthesized from Audiophile, Dev PM, Audio Product, and Project Management perspectives*

---

## Current State Assessment

| Metric | Value | Notes |
|--------|-------|-------|
| **Tests Passing** | 3,175 | Comprehensive coverage |
| **Source Files** | 150+ | Core + 15 formats |
| **Formats Complete** | 15 of 17 | All major formats with full read/write |
| **Format Coverage** | ~88% | Speex, TrueAudio remaining |
| **Core Infrastructure** | ✅ 100% | Tag, BinaryData, IFileSystem, Picture, MediaFile factory |

---

## Revised Priority Order

Based on multi-perspective analysis, the original roadmap is reordered:

| Original Priority | Revised Priority | Format | Rationale |
|-------------------|------------------|--------|-----------|
| P0-1 WAV | **✅ COMPLETE** | WAV | Full read/write with RIFF INFO + ID3v2 + bext (BWF) |
| P0-3 MP4/M4A | **✅ COMPLETE** | MP4/M4A | Full read/write with iTunes atoms + AAC/ALAC |
| P0-7 VBR Headers | **✅ COMPLETE** | VBR | Xing/VBRI header parsing for accurate MP3 duration |
| P0-6 APE Tag | **P0-3 APE Tag** | Same | Blocks WavPack/Musepack/Monkey's Audio |
| P0-5 Opus | **✅ COMPLETE** | Opus | Full read/write with OpusHead + OpusTags + R128 gain |
| P0-2 AIFF | **✅ COMPLETE** | AIFF | Full read/write with FORM container + COMM + ID3 |
| P0-4 DSF | **P0-5 DSF** | ⬇️ Demoted | DSD is vocal minority, not market size |
| P1 Musepack | **SKIP** | ❌ Remove | Dead format (last release 2009) |

---

## Milestone Map

### Milestone 1: Technical Debt & Infrastructure
**Duration:** 1 week | **Status:** ✅ Complete

Fix blocking issues before new formats:

| Task | Effort | Status |
|------|--------|--------|
| Extract `EndianReader.cs` static class | 2h | 🔶 In BinaryData (adequate for now) |
| Extract `SyncsafeInteger.cs` static class | 1h | 🔶 In Id3v2Tag (adequate for now) |
| Create `ExtendedFloat.cs` (80-bit IEEE 754) | 4h | ✅ Complete |
| Fix DSD duration overflow (use double) | 1h | ❌ Not started (DSF not yet implemented) |
| Format detection factory | 4h | ✅ Complete (MediaFile.Read) |
| Complete IDisposable pattern | 4h | ❌ Not started |

**Exit Criteria:**
- ✅ All utility classes extracted and tested
- ❌ No integer overflow on DSD files (DSF not yet implemented)
- ✅ Format detection working for all formats (MediaFile factory)

---

### Milestone 2: MP3/FLAC Write Support
**Duration:** 1-1.5 weeks | **Status:** ✅ Complete

Complete the formats we already read:

| Task | Effort | Dependencies |
|------|--------|--------------|
| MP3 ID3v2 write | 3d | ✅ Complete |
| MP3 ID3v1 write | 1d | ✅ Complete |
| MP3 VBR header parsing (Xing/VBRI) | 2d | ✅ Complete |
| FLAC metadata block write | 3d | ✅ Complete |
| FLAC padding management | 1d | ✅ Complete |
| Round-trip tests (MP3, FLAC) | 2d | ✅ Complete |

**Exit Criteria:**
- ✅ Read → Modify → Write → Read produces identical data
- ✅ MP3 duration accurate for VBR files
- ✅ Cross-tagger compatibility (foobar2000, Mp3tag)

---

### Milestone 3: MP4/M4A (Critical Path)
**Duration:** 1.5-2 weeks | **Status:** ✅ Complete | **Complexity:** 10/10

Highest business value (Apple ecosystem):

| Task | Effort | Notes |
|------|--------|-------|
| Atom tree navigation | 3d | ✅ moov/udta/meta/ilst path |
| Standard atoms (©nam, ©ART, ©alb, etc.) | 2d | ✅ Text atoms |
| trkn/disk parsing | 1d | ✅ Track/disc number pairs |
| covr atom (cover art) | 1d | ✅ JPEG/PNG images |
| Freeform atoms (----/mean/name/data) | 2d | ✅ Custom fields, MusicBrainz, ReplayGain |
| Audio properties from stsd/mvhd | 1d | ✅ Duration, sample rate, bitrate, channels |
| AAC esds parsing | 1d | ✅ Complete |
| ALAC magic cookie | 1d | ✅ Complete |
| Atomic file write | 1d | ✅ Complete with mdat relocation |
| MediaFile factory integration | 0.5d | ✅ Complete |
| Round-trip tests | 2d | ✅ iTunes compatibility verified |

**Exit Criteria:**
- ✅ iTunes-tagged files read correctly
- ✅ Write operations don't break iTunes compatibility
- ✅ ALAC and AAC variants both work
- ✅ MediaFile.Read auto-detects MP4/M4A format

**Risks Mitigated:**
- ✅ Complex atom tree structure - handled
- ✅ Extended size atoms (>4GB files) - supported
- ✅ iTunes quirks - compatible

---

### Milestone 4: WAV & RIFF Infrastructure
**Duration:** 1 week | **Status:** ✅ Complete

Studio format + shared container:

| Task | Effort | Notes |
|------|--------|-------|
| RIFF container parser | 2d | ✅ Complete |
| RIFF INFO tags | 1d | ✅ Complete (INAM, IART, IPRD, etc.) |
| WAV fmt chunk parsing | 1d | ✅ Complete + WAVEFORMATEXTENSIBLE |
| WAV ID3v2 chunk support | 1d | ✅ Complete |
| WAV bext chunk (BWF) | 1d | ✅ Complete |
| WAV write support | 2d | ✅ Atomic writes |
| Round-trip tests | 1d | ✅ Complete |

**Exit Criteria:**
- ✅ WAV files with RIFF INFO tags read correctly
- ✅ WAV files with ID3v2 chunks read correctly
- ✅ Write operations use atomic writer

---

### 🎯 ALPHA RELEASE (v0.1.0) - ✅ RELEASED 2025-12-26
**Status:** Released

**Formats:** MP3, FLAC, OGG Vorbis, WAV, AIFF (5 formats with full read/write)

**Quality Bar:**
- [x] All 5 formats pass round-trip tests
- [x] 1,000+ tests
- [x] Cross-tagger compatibility verified
- [x] No known data-loss bugs
- [x] NuGet package published

### 🎯 v0.2.0 - ✅ RELEASED 2025-12-29

**Added:**
- ID3v2.2 legacy support (3-char frame IDs)
- ID3v2 unsynchronization and frame flags (compression, grouping)
- FLAC MD5 audio signature
- BWF (bext chunk) support for WAV
- WAVEFORMATEXTENSIBLE support
- Ogg CRC validation option
- Picture support for WAV and AIFF
- AIFF write support

### 🎯 v0.2.1 - ✅ RELEASED 2025-12-29

**Added:**
- Error context for file parsing (WavFileReadResult, AiffFileReadResult)
- Test coverage: Polyfills, OggCrc, Id3v1Genre (+51 tests)
- 1,939 total tests

### 🎯 v0.3.0 - ✅ RELEASED 2025-12-30

**Added:**
- Ogg Opus full support (read/write)
- OpusHead parsing per RFC 7845
- R128 gain tags (output gain, album gain, track gain)
- Multi-stream support (mapping families 0, 1, 255)
- Stream/coupled count validation
- Security validation (max packet size, parameter checks)
- 2,078 total tests

### 🎯 v0.4.0 - ✅ RELEASED 2025-12-31

**Added:**
- **MP4/M4A full support** (read/write)
- ISO 14496-12 box parsing (ftyp, moov, mdat, etc.)
- iTunes-style metadata atoms (ilst)
- AAC audio properties via esds parsing
- ALAC audio properties via magic cookie
- Album art (covr atom) with JPEG/PNG detection
- MusicBrainz IDs and ReplayGain via freeform atoms
- Atomic file writing with mdat relocation
- **MediaFile factory** with format auto-detection
- 2,318 total tests (88.67% code coverage)

---

### Milestone 5: Opus & APE Tag
**Duration:** 1 week | **Status:** ✅ Complete

Modern lossy + infrastructure for P1 formats:

| Task | Effort | Notes |
|------|--------|-------|
| Opus OpusHead parsing | 1d | ✅ Complete |
| Opus OpusTags parsing | 1d | ✅ Complete |
| Opus R128 gain handling | 0.5d | ✅ Complete (OutputGain, R128TrackGain, R128AlbumGain) |
| Multi-stream support | 0.5d | ✅ Complete (mapping families 0, 1, 255) |
| APE Tag v2 format | 3d | ✅ Complete |
| APE tag in MP3 | 1d | ✅ Complete |
| Round-trip tests | 1d | ✅ Complete |

**Exit Criteria:**
- ✅ Opus files read correctly
- ✅ R128 gain values properly exposed
- ✅ APE tags read/write in isolation
- ✅ MP3 with APE+ID3v2+ID3v1 handled correctly

---

### Milestone 6: AIFF & DSF
**Duration:** 1 week | **Status:** ✅ Complete

Completing P0 formats:

| Task | Effort | Notes |
|------|--------|-------|
| AIFF FORM container | 1d | ✅ Complete |
| AIFF COMM chunk (80-bit float) | 1d | ✅ Complete (ExtendedFloat) |
| AIFF ID3 chunk | 1d | ✅ Complete |
| AIFF AIFC compression support | 0.5d | ✅ Complete |
| AIFF write support | 1d | ✅ Complete |
| DSF DSD/fmt chunks | 1d | ✅ Complete |
| DSF ID3v2 at offset | 1d | ✅ Complete |
| Round-trip tests | 1d | ✅ Complete |

**Exit Criteria:**
- ✅ AIFF sample rate parsed correctly
- ✅ DSF metadata at end of file works
- ✅ DSD duration calculation correct

---

### 🎯 BETA RELEASE (v0.5.0) - ✅ RELEASED 2026-01-03
**Status:** Released

**Formats:** 15 formats with full read/write support

**Completed:**
- [x] DSF format support (DSD)
  - DSD/fmt chunk parsing
  - ID3v2 at metadata offset
  - Duration calculation (use double to avoid overflow)
- [x] DFF (DSDIFF) format support
  - FRM8/DSD chunk parsing
  - ID3v2 metadata at end of file
- [x] APE Tag format (infrastructure for WavPack/Monkey's Audio)
  - APE v2 tag parsing
  - Binary item support
  - Cover art support
- [x] ASF/WMA full read/write support
- [x] Musepack (SV7/SV8) with APE tags
- [x] WavPack with APE tags
- [x] Monkey's Audio with APE tags
- [x] Ogg FLAC with Vorbis Comments
- [x] DSF/DFF write support
- [x] IDisposable pattern for BinaryDataBuilder
- [x] Test coverage 90.1%
- [x] Large file tests (>4GB)

**Quality Bar Achieved:**
- [x] 15 formats with full read/write
- [x] 3,175 tests passing
- [x] 90.1% line coverage, 77.6% branch coverage
- [x] Large file support verified (>4GB)
- [x] Zero known data-loss bugs

---

### Milestone 7: P1 Extended Formats
**Duration:** 2-3 weeks | **Status:** ✅ Mostly Complete

Legacy and niche formats:

| Format | Effort | Status |
|--------|--------|--------|
| WMA/ASF | 5-6d | ✅ Complete (full read/write) |
| DFF | 1-2d | ✅ Complete (ID3v2 metadata) |
| WavPack | 3d | ✅ Complete (APE tags) |
| Ogg FLAC | 2d | ✅ Complete (Vorbis Comments) |
| Musepack | 2d | ✅ Complete (APE tags) |
| Speex | 1d | ❌ Not started |

**Note:** Musepack was implemented despite being a legacy format

---

### Milestone 8: P2 Niche Formats
**Duration:** 1-2 weeks | **Status:** Not Started

Edge cases only if requested:

| Format | Effort | Notes |
|--------|--------|-------|
| TrueAudio | 2d | ID3v2/ID3v1 |
| Monkey's Audio | 2d | APE tags |
| Tracker formats (MOD/S3M/IT/XM) | 3d | Title only |

---

### Milestone 9: Polish & Release
**Duration:** 2-3 weeks | **Status:** Not Started

Production readiness:

| Task | Effort |
|------|--------|
| Performance optimization pass | 3d |
| Memory leak audit | 2d |
| TagLib# compatibility shim | 3d |
| Complete XML documentation | 3d |
| Migration guide | 2d |
| Example projects | 2d |
| 9 known spec issues fixed | 2d |

---

### 🎯 PRODUCTION RELEASE (v1.0.0)
**Target:** Week 18-20 from start

**Formats:** 22 total (or documented limitations)

**Quality Bar:**
- [ ] >95% test coverage
- [ ] Zero known critical bugs
- [ ] Real-world corpus tested (1,000+ files)
- [ ] 4+ weeks production usage
- [ ] Community adoption (3+ projects)

---

## Timeline Summary

```
✅ v0.1.0 RELEASED: 2025-12-26 (MP3, FLAC, OGG Vorbis, WAV, AIFF)
✅ v0.2.0 RELEASED: 2025-12-29 (ID3v2.2, unsync, BWF, WAVEFORMATEXTENSIBLE)
✅ v0.2.1 RELEASED: 2025-12-29 (Error context, test coverage)
✅ v0.3.0 RELEASED: 2025-12-30 (Ogg Opus with R128 gain)
✅ v0.4.0 RELEASED: 2025-12-31 (MP4/M4A + MediaFile factory)
✅ v0.5.0 RELEASED: 2026-01-03 (DSF, DFF, ASF/WMA, Musepack, WavPack, Monkey's Audio, Ogg FLAC, APE Tag)

CURRENT STATUS: 15 formats with full read/write support

NEXT UP (v0.6.0):
- Speex format (Ogg container ready)
- TrueAudio format (if requested)
         >>> RELEASE CANDIDATE (v0.9.0) <<<
- TagLib# compatibility shim
- Performance optimization pass
         >>> PRODUCTION RELEASE (v1.0.0) <<<
```

---

## Key Decisions Made

### What We're Prioritizing

1. **MP4/M4A moved to P0-1** - Apple ecosystem is too large to ignore
2. **VBR Headers moved to P0-3** - Broken MP3 duration is unacceptable UX
3. **Classical metadata** - Work/Part/Movement hierarchy is a differentiator
4. **Both ReplayGain and R128** - Industry hasn't converged
5. **MusicBrainz IDs** - High adoption, critical for matching

### What We're Deprioritizing

1. **AIFF/DSF demoted within P0** - Smaller user bases than WAV/MP4
2. **Musepack SKIPPED** - Dead format (last release 2009)
3. **MQA detection** - MQA is dead, just read ENCODER tag naturally
4. **Tracker formats** - Niche retro community
5. **Video formats** - Outside music library scope

### Architecture Decisions

1. **Extract utility classes first** - EndianReader, SyncsafeInteger, ExtendedFloat
2. **RIFF container before WAV** - Reuse for AVI if needed later
3. **APE tag format before WavPack/Musepack** - Unlocks multiple formats
4. **Format detection factory early** - Better extensibility

---

## Risk Mitigation

| Risk | Severity | Mitigation |
|------|----------|------------|
| MP4 complexity | HIGH | Extra time allocated (2 weeks) |
| Cross-tagger quirks | MEDIUM | Test with real files from foobar2000, iTunes, Picard |
| Large file overflow | MEDIUM | Use double for DSD duration, long for offsets |
| Clean room contamination | CRITICAL | Reference specs only, not TagLib# source |
| Data loss on write | CRITICAL | AtomicFileWriter already implemented |

---

## Success Metrics

### Alpha Release (v0.1.0) - ✅ ACHIEVED
- ✅ 5 formats working (MP3, FLAC, OGG Vorbis, WAV, AIFF)
- ✅ 1,000+ tests passing (now 1,939)
- ✅ <10ms tag reading performance
- ✅ Zero data loss bugs

### Beta Release (v0.5.0) - ✅ ACHIEVED
- ✅ 15 formats with full read/write
- ✅ DSF, DFF, ASF/WMA, Musepack, WavPack, Monkey's Audio, Ogg FLAC added
- ✅ APE Tag format ready (infrastructure for future formats)
- ✅ IDisposable pattern for BinaryDataBuilder
- ✅ 3,175 tests passing
- ✅ 90.1% line coverage
- ✅ Large file support verified (>4GB)

### Production Release (v1.0.0)
- 15+ formats (Speex/TrueAudio optional)
- TagLib# compatibility shim
- 4+ weeks production stability
- Community adoption

---

## Resource Estimate

| Phase | Developers | Duration |
|-------|------------|----------|
| Alpha (5 formats) | 1-2 | 6-8 weeks |
| Beta (8 formats) | 1-2 | 10-12 weeks |
| Production (22 formats) | 1-2 | 18-20 weeks |

**Parallelization:** With 2 developers, alpha could be 4-5 weeks.

---

*Last Updated: 2026-01-03 (v0.5.0 released)*
*Sources: Audiophile analysis, Product Manager analysis, Project Manager analysis*
