# Changelog

All notable changes to TagLibSharp2 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Breaking Changes

- **Changed**: `AiffFile.Tag` is now a get-only `Tag?` view over the ID3 chunk and the native AIFF text chunks (it was a settable `Id3v2Tag?`). Assign or read the ID3 tag through the new `AiffFile.Id3v2Tag` property; code that assigned `AiffFile.Tag` or used ID3-only members on it no longer compiles. `AiffFile.Tag.Render()` throws `NotSupportedException`, as the other combined views do; render the `AiffFile` instead
- **Changed**: `Id3v2Tag.IsEmpty` is false when the tag holds any frame, including lyrics, ratings, pictures, comments, user text, URLs and private frames. It used to check only the basic fields, so WAV and AIFF saves dropped an ID3 chunk that held only, for example, a composer, a publisher or a BPM

### Added

- `Tag.GetCustomField`, `Tag.SetCustomField`, `Tag.RemoveCustomField`, and `Tag.SupportsCustomFields` read,
  write, and remove a named field with one or more values on any tag (#112). The field lives in the format's
  own storage: a Vorbis comment field, an APE text item, an ID3v2 `TXXX` frame, an MP4 `----:com.apple.iTunes`
  freeform atom, an ASF attribute, a file-level Matroska `SimpleTag`, or a RIFF INFO chunk (Latin-1 in WAV;
  any text in AVI, saved under the file's CSET rules). ID3v1 reports no support. `CombinedTag` (MP3, raw AAC,
  AVI with an ID3v2 chunk, MPEG program streams), `FlacTag`, and the AIFF view (`AiffFile.Tag`) forward the
  members. A combined view writes the values into every tag that can store them and removes the name from a
  tag that cannot (RIFF INFO holds one value), so no tag keeps an older value. On ASF the first value goes to
  the Content Description or Extended Content Description and the others to the Metadata Library Object, as
  Windows writes them. `GetCustomField`, `Performers`, `AlbumArtists`, `Composers`, and `Genres` read every
  value; `SetCustomField` and those four properties write them back the same way; a remove, a mapped setter
  given a new value, and `Clear` drop the Metadata Library values too. ASF follows the write-back rule of
  ID3v2, APE, and Vorbis Comments (`Tag.IsWriteBack`): writing back the exact value a mapped property read
  changes nothing, `null` always clears, a stored value the getter cannot read included (the DWORD year
  mutagen writes, say), and `CopyTo` always replaces. A single-value ASF getter returns the first non-empty
  value of its attribute (a string, a number, or a BOOL), from the Content Description or Extended Content
  Description and then the Metadata and Metadata Library Objects, as the ID3v2, APE, and Vorbis Comment
  getters do, and `IsCompilation = false` clears a true stored in any of them.
- `MatroskaFile.Tracks` lists the first 4096 known-size `TrackEntry` elements as `MatroskaTrack` objects, and `MatroskaFile.IsTrackListTruncated` tells when a file had more. Each track has `TrackNumber`, `TrackUid`, `TrackType`, `CodecId`, `IsDefault`, and `IsEnabled` (RFC 9559 §5.1.4.1).
- `MatroskaTrack.IsEnabled` is written back on save. A stored `FlagEnabled` is rewritten in place. A missing one is added to the `TrackEntry`, taking the room from the `Void` right after `Tracks` when it has enough so no element moves; otherwise everything after `Tracks` moves once, with every SeekHead, Cues and Cluster `Position` value and CRC-32 updated, each widened when it no longer fits. Adding a FlagEnabled (minver 2) also makes the EBML header's `DocTypeVersion` at least 2: a stored lower value is raised, and an empty or missing one is written (RFC 9559 section 7); no Segment Position moves. A track the caller did not set keeps its bytes.
- `AiffFile.Id3v2Tag`, and `AiffFile.Title`, `Artist`, `Comment` and `Copyright`
- MPEG program streams and MPEG video elementary streams (`.mpg`, `.mpeg`, `.mpe`, `.vob`, `.mpv`, `.m1v`, `.m2v`): `MpegPsFile` and `MediaFormat.MpegPs`. Reads the systems version (ISO/IEC 11172-1 or 13818-1), MPEG-1/MPEG-2 video size, frame rate and bit rate, the first audio stream (MPEG audio, AC-3, LPCM; DTS identified), and the duration from the first and last time stamps, reading only the head and the tail of the stream. Elementary video takes its duration from GOP time codes. An ID3v2 tag in front of the stream, an ID3v2.4 tag appended after it with a `3DI` footer (the layout TagLib# writes; ID3v2.4 structure 3.4 and 5), and an ID3v1 tag are read and written where they are, leaving one ID3v2 tag; the stream bytes are copied unchanged and a save with no tag change is byte-identical. A file that starts with ID3v2 followed by a pack header opens as a program stream, not MP3 (#108)
- MP4 QuickTime metadata whose `meta` handler is `mdta` is read and written through its `keys` atom, in `moov/udta/meta` (where ffmpeg's `-movflags use_metadata_tags` writes it) and in `moov/meta` (where QuickTime and Apple devices write it). The handler and each key's namespace are checked first ([QTFF metadata handler atom](https://developer.apple.com/documentation/quicktime-file-format/metadata_handler_atom)). Apple's `com.apple.quicktime.*` keys that have a property (title, displayname, artist, album, comment, composer, copyright, description, encodedby, genre, publisher, year, software, keywords, artwork), ffmpeg's names (title, artist, album, album_artist, composer, date, comment, genre, copyright, description, grouping, lyrics, encoder, encoded_by, publisher, the five sort_* fields, category, keywords, track and disc as `n/m`, compilation, gapless_playback, podcast, rating) and every other name outside `com.apple.quicktime.*` as a freeform field (com.apple.iTunes, or org.musicbrainz for the MusicBrainz names; another mean is written as `mean:name`, which reads back only for a mean made of letters, digits, `.`, `-` and `_` with at least one dot, such as `org.example`; any other mean comes back as part of the name) map to their properties, and a `udta`-namespace key to its atom. Unmapped `com.apple.quicktime.*` keys (make, model, location, creationdate and the like) are kept as they are. A set replaces every item its field was read from (keyed items, an iTunes atom a lenient writer such as mutagen added beside them, and items the reader could not take, such as a vinyl track `A1`) with one item at the field's key index, where the first one was; a field with no key gets one named the way the file names its keys. No iTunes atom is written into an `mdta` meta; one found there is kept until its field is set. In `udta/meta` it wins over the keyed item, being the newer edit (mutagen adds one when it edits an ffmpeg file); in `moov/meta` the keyed item wins, as in AVFoundation, which ignores iTunes atoms there. An unedited save writes every item back as it was, in order. A `moov/meta` with an `mdta` handler beside `udta/meta` (an ffmpeg file that Apple software or ExifTool gave keys) is merged in and wins for the fields it declares, since QTFF gives metadata precedence over user data that declares the same information; AVFoundation reads it the same way. A set writes the new value to both metas, so every reader shows it. Assigning a tag or copying one in from another file replaces only the mapped fields, in every meta the file has: items the tag does not map (Apple's make, model, location and creation date, any other unmapped key, an unknown iTunes atom such as `orch`, an iTunes Store purchase's `apID`, `purd`, `cnID` and `stik`) are kept as they are. Copied into an iTunes ilst, an iTunes tag brings its own unknown atoms (replacing the target's atom of the same type); a keyed meta takes no iTunes atom, and a keyed tag's items mean nothing without its keys, so those stay behind. `Clear()` and `Tag = null` remove all metadata of every meta, mapped or not, including those unmapped items, their keys, and a damaged `keys` atom (which would otherwise keep ffmpeg from opening the file). They also remove the iTunes gapless info (`iTunSMPB`, `GaplessInfo`): in a file without an edit list, players that rely on it then play the encoder's priming and padding samples, as after an ExifTool or mutagen strip. A `moov/meta` with another handler (an iTunes ilst) is not QuickTime metadata and is not read; its copies of mapped fields (`©nam`, `covr`, `----` and the rest) are dropped by a set of that field and by an assigned tag, and its other atoms are kept until `Clear()` or `Tag = null`. A damaged `keys` atom maps nothing and is kept as it is until the tag is cleared. TagLib# and mutagen do not read keyed items.
- `AudibleFile` and `AudibleTag` read and write Audible `.aa` audiobooks: the text dictionary (title, author, narrator, provider, dates, descriptions, keywords, copyright, short and parent titles, product and title IDs, and any other key through `GetValue`/`SetValue`), the cover image, and the duration. A save rewrites the dictionary and moves every table-of-contents offset, the declared file size, and the cover position by the change in its size. The encrypted audio and the DRM keys are copied unchanged; `SetValue` refuses `codec`, `HeaderSeed`, `HeaderKey`, and `EncryptedBlocks`, and `Clear` keeps them. `MediaFormat.Audible` and `TagTypes.Audible` are new, and `.aa` is detected by its magic or extension. Edited `.aa` files have been checked with ffprobe, ffmpeg, and ExifTool, not with Audible's own apps, which may check a digest the file stores; keep a copy of the original or save to a new path.
- `.aax` opens as MP4 by extension as well as by content. `Mp4Tag` and `Mp4File` gain `Narrator` (`©nrt`), `ShortTitle` (`@sti`), `ParentTitle` (`@pti`), `ParentShortTitle` (`@PST`), `ParentProductId` (`@ppi`), `ProductId` (`prID`), `ReleaseDate` (`rldt`), and `ProductVersion` (`VERS`). An Audible `aavd` sample entry reports AAC.
- Matroska: setting `Pictures` writes image attachments (RFC 9559 sections 5.1.6 and 21.1). A new front cover is named `cover.` plus the extension of its media type (`cover.jpg`, `cover.png`; another image type such as GIF becomes `cover.gif`, although RFC 9559 recommends only JPEG and PNG for covers). It goes first unless the file keeps its normal cover (`cover.*`); that one keeps its place and the new one goes after the kept attachments. A kept `small_cover` or `cover_land` is not the normal cover. Other picture types get names such as `back.jpg` or `artist.png`, which read back as their type. The description goes in `FileDescription`. An attachment that matches a picture, compared with the media type as stored, keeps its `FileName`, `FileUID` and bytes. A new `FileUID` is random, nonzero and different from every `FileUID` in the file. Fonts and other non-image attachments are kept. Removing a picture removes only its attachment and any tag that targeted only that attachment.
- Matroska: a new or moved `Attachments` element goes after the last Cluster or in a Void, and gets an entry in the first SeekHead. The SeekHead can widen its size field to grow into the Void after it.
- Matroska/WebM chapters (#114): `MatroskaFile.Chapters` reads and writes editions (`MatroskaEdition`) and nested chapter atoms (`MatroskaChapter`) with UID, string UID, start and end time, display strings with languages and countries (`MatroskaChapterDisplay`), and the enabled and hidden flags (RFC 9559 section 5.1.7). New UIDs are random and nonzero. Chapter titles never become `Tag.Title`. WebM saves write only the chapter elements WebM supports, edited or not. A save fails when a chapter it adds or changes ends before it starts, starts outside its parent, or lacks an end in an ordered edition (RFC 9559 section 20.2); the error names the chapter by position and UID. A chapter that was not changed is not checked and, in a Matroska file, is written back byte for byte; one moved to another edition or parent, or in an edition whose own values changed, is checked with everything nested in it. An added edition without chapters is not an edit. Atom nesting (32 levels) and parsed children (65,536) are bounded; the rest is kept as one copy of its bytes, so memory stays linear. `Clear()`, `RemoveEdition`, and a removal through `Editions` remove a `Chapters` element even when it held no chapter atoms. An edited `Chapters` element goes in front of the first Cluster (RFC 9559 section 6.6), moving what follows, so a reader that goes front to back finds it; with an ordered edition it goes after the first Info and before the first Tracks, as section 6.6 asks, when the file has them in that order.
- **MP4 chapters** (#113): `Mp4File.Chapters` reads and replaces the chapter list, with the new `Mp4Chapter`
  type (start time, title, derived end time). Reading takes the QuickTime chapter track (the text track named
  by a `tref`/`chap` reference), then the Nero `chpl` box. Saving writes both: `chpl` (first 255 chapters) and
  the chapter track (an existing one keeps its track ID; otherwise a new disabled text track is added and
  referenced from every sound and video track). New chapter samples go in their own `mdat` after `moov`, and
  every other track's chunk offsets move with them. That `mdat` replaces the one an earlier chapter save wrote,
  so repeated edits do not grow the file. A save that leaves the list alone keeps the chapter bytes.
  Chapter samples in movie fragments are read; changing chapters whose track has, or may have, samples in movie
  fragments fails with an error instead of leaving a second list. A replaced track keeps its language, handler,
  and flags. New tracks use `nmhd` and `tx3g` in MP4 (Apple TN2174) and `gmhd` and the QTFF `text`
  description in QuickTime files (major brand `qt  `, or no `ftyp`). A chapter sample that is not valid
  UTF-8/UTF-16 text yields to `chpl`. Clearing also removes Apple chapter image and URL tracks.
  A set replaces every chapter title track with the one list: other title tracks (for example one per
  language) are removed, and a track whose chapter reference named one then names the rebuilt track. A
  chapter URL track (samples with an `href` modifier, 3GPP TS 26.245 §5.17.1.5) and chapter image tracks stay.
  Chapters and the tag are separate: `ClearAllMetadata()` and `Tag = null` keep the chapters, and
  `Chapters = []` keeps the tag.
- `AviFile` reads and writes AVI files (RIFF `AVI ` form, including OpenDML `odml`/`indx` and `AVIX` forms). `MediaFile.Read` detects them as `MediaFormat.Avi`; WAV stays the `WAVE` form.
  - Properties: duration (OpenDML `dmlh` total frames when present, timed by the video stream's rate and scale), video width, height, frame rate and BITMAPINFOHEADER FourCC, and the first audio stream's WAVEFORMATEX sample rate, channels, bits and bitrate.
  - Tags: every `LIST INFO` in the first RIFF form (`InfoTag`) and an ID3v2 tag in an `ID32` chunk (`Id3v2Tag`). The unified `Tag` puts INFO first.
  - Saving keeps the `movi` sample bytes. New tags reuse the tag and `JUNK` space between `hdrl` and `movi`, so nothing moves; when they do not fit, the data moves and the RIFF size, absolute `idx1` offsets, `indx` super index entries, and each standard index's base offset (or, when the base stays put, each entry whose data moved) are shifted. A save is refused, without writing, when an index cannot be verified first. A changed tag chunk cut off by the end of the file is turned into `JUNK`, so a removed value does not return. An unchanged save writes the original bytes.
  - A file whose RIFF size is still the streaming placeholder 0xFFFFFFFF reports no duration: its frame counts were never filled in.
  - AVI INFO text honours a `CSET` chunk (code page 65001, 1252, 0 or 28591); without one, valid UTF-8 (ffmpeg's and TagLib#'s encoding) is read as UTF-8 and other text as Windows-1252. On save, an unchanged value keeps its exact bytes and the file keeps its `CSET` (or its absence); changed values are written in the file's code page when they fit it (ASCII only when there is no `CSET`), otherwise the list becomes UTF-8 with a `CSET` of code page 65001 before it. When the list becomes UTF-8, untouched text in a declared code page decoded exactly (1252, 28591, 0) is rewritten as the same text in UTF-8; a save that would have to rewrite untouched text whose code page is a guess (no `CSET`), is not decoded here (for example Shift-JIS or cp1251), or that holds a byte its declared code page leaves undefined (0x81, 0x8D, 0x8F, 0x90, 0x9D in 1252; 0x80-0x9F under code page 0) is refused, and the error names the field and which of those reasons applies. A CSET rewritten to 65001 keeps the file's country, language and dialect codes. The five undefined 1252 code points are never written as 1252 bytes; a value holding one goes to UTF-8. WAV INFO is unchanged (Latin-1).
  - A path save copies `movi` in 64 KB pieces and patches `idx1` and OpenDML indexes from the source, so it no longer loads the file. `Render` and `SaveToFile(path, span)` still build one buffer. `MediaFile.Read` and `AviFile.ReadFromFile` read only the header and tag chunks.
- Matroska `AlbumArtistSort` and `ComposerSort`: `SORT_WITH` nested in the album-level `ARTIST` (`TargetTypeValue` 50) and in `COMPOSER`, with flat `ALBUM_ARTIST_SORT` / `COMPOSER_SORT` as the fallback. Clearing removes both forms at every level the getter reads. When the track inherits the album's `ARTIST`, writing either `ArtistSort` or `AlbumArtistSort` first copies that `ARTIST` to the track level, so the other sort is kept (#116).
- Still images: `MediaFile.Read` and `StillImageFile.Read`/`ReadFromFile`/`ReadFromFileAsync` open JPEG, PNG, GIF, BMP, PCX, Netpbm (PBM, PGM, PPM, PNM), TIFF, DNG, and SVG and report width and height. A TIFF or DNG reports the size exiftool's ImageSize gives (measured case by case): the width and height of the first IFD whose NewSubfileType is 0, the full-resolution image, in the order exiftool reads them (IFD0, its SubIFDs depth first with their next IFDs, then IFD0's next IFD), or else IFD0's; unlike exiftool, a width and a height come from one IFD, and a size of 0, a negative one or one past 2^31 - 1 is none. DNG is TIFF with the DNG version tag in IFD0. A table of any size whose entries fit in the data is read, as exiftool reads one of 65,535 entries, and SubIFD offsets are decoded as exiftool decodes them, with the same rules as the Exif pointers below (in BigTIFF also LONG8, SLONG8 and IFD8, but not UNDEFINED); the walk reads at most 4,096 IFDs, 64 SubIFD levels deep, and stops once it has read more entries than tables that do not overlap could hold. A classic IFD is read when 0, 2 or 4 bytes of its next-IFD field are present and is not read when 1 or 3 are (exiftool 13.55 drops that directory), When that directory is IFD0 the file is not opened, and the error is "IFD0's next-IFD field has 1 of its 4 bytes, so IFD0 is not read and the image has no size" (or 3 of its 4 bytes). A wholly missing next field is still read, with no next IFD. A JPEG whose frame height is 0 takes it from the DNL segment after the first scan (ITU-T T.81 §B.2.5). PNG text keywords (Title, Author, Description, Copyright, Creation Time, Comment) are written before the pixel data, the GIF comment after the header, and the JPEG COM marker before the scan; exiftool's PNG `Artist` keyword is read as the artist when there is no `Author`. `Render` copies every other byte, including the pixels and anything after the PNG IEND, the GIF trailer, or the JPEG EOI; a no-op save is byte-identical. A path save (`SaveToFile(path)` and `SaveToFileAsync`) copies those pixels in 64 KB pieces and writes the same bytes as `Render`. When a field changes, its copies elsewhere in the file change too: the XMP property (`dc:title`, `dc:creator`, `dc:description`, `dc:rights`, `exif:UserComment`) is replaced in place, keeping its start and end tags, or removed for null, in PNG `XML:com.adobe.xmp` and ImageMagick's `Raw profile type xmp`, JPEG APP1 XMP, and the GIF XMP extension; the Exif copies (IFD0 `ImageDescription`, `Artist`, `Copyright`, the Windows XP title, author and comment tags, and `UserComment`) are removed from PNG `eXIf`, ImageMagick's raw EXIF, and JPEG APP1 Exif, from every table exiftool reads with its Exif tags (measured with exiftool 13.55): IFD0 and the IFDs chained after it (IFD1, IFD2, ...), and every Exif IFD (0x8769), SubIFD (0x014A) and interoperability IFD (0xA005) any of them points at, however deep (exiftool shows a copy from any of them; ImageIO and TagLib# read a subset), each table walked once. A pointer is followed as exiftool decodes it: a BYTE, SHORT, LONG, SBYTE, SSHORT, SLONG or IFD value, or UNDEFINED with count 1; an Exif IFD pointer only with count 1, a GPS or interoperability pointer by its first value, SubIFDs by every value; a negative value, or values that do not all lie in the data, name nothing; two pointer arrays at one offset are each read by their own type. IFD0's Exif IFD and GPS pointers are also followed as TagLib# reads them, their 4-byte field taken as a LONG whatever their type and count (measured), so the copy TagLib# shows from such a table is removed too, and its GPS table is protected. A GPS table is read with its GPS tags only: nothing in it is edited and no pointer in it is followed. A table is walked when its entries fit in the data. A pointer whose table's entries do not fit is skipped, as readers skip it. A field edit runs only when that structure is plain, and nothing is written otherwise. It is not plain when a strip, tile or thumbnail offset or length (TIFF 6.0 tags 0x0111/0x0117, 0x0144/0x0145 and 0x0201/0x0202) is typed other than SHORT or LONG; when one of those arrays is named more than once (two tables, both sides of one pair, or the same tag twice in one table); when a complete pair names more values than half the bytes; when a classic next-IFD field has only one, two or three of its four bytes (a wholly absent next field stays editable; exiftool itself still reads a table that has two); when removing an entry would change whether exiftool reads that table's first entry, including a change no tag shows; or when a removed value shares a protected byte, or compacting a table would rewrite any protected range other than that table. Two protected ranges may share a byte. Clear does not use this gate. An edit with nothing to remove still saves. A save is also refused when the tables hold more entries than the data could without overlapping and the block's bytes could hold an entry with one of the field's tags, of any type; edits with nothing to remove save. The `Artist` and ImageMagick `exif:*` text keywords are dropped. A packet no XMP reader reads because it is not well-formed, and does not name a changed field, an ImageMagick raw profile that is not hex, and PNG text that is not zlib data or whose iTXt header is cut short, which no reader decodes, are kept as they are; compressed text past the shared inflation budget is refused, since it cannot be checked. For a JPEG or GIF only the comment is stored, so only the comment's copies change. An XMP packet followed by NULs or padding (as ImageIO writes it) is edited and the tail kept, and GIF XMP written as data sub-blocks (ImageIO) is edited as well as the raw form with its magic trailer. A save that cannot update a copy (XMP that is not well-formed or has a DTD and names a changed field's property, a value XML cannot hold, an Exif block that is not classic TIFF and whose bytes could hold an entry with one of the field's tags, in either byte order, a JPEG XMP packet that would outgrow its segment, extended XMP, or a GIF XMP block in neither the raw form with its magic trailer nor data sub-blocks, that holds a UserComment) is refused with its own reason, as is a container that cannot be spliced, or a multi-picture JPEG whose MPF index cannot be corrected or (for Clear) followed; the file is left unchanged. Inflated PNG text is capped at 1 MiB for the whole file, on read and on save. `Clear` removes all tag metadata and keeps only what a decoder or colour management needs, or the file's structure: PNG critical chunks and tRNS, gAMA, cHRM, sRGB, iCCP, cICP, mDCV, cLLI, sBIT, bKGD, hIST, pHYs, sPLT, the APNG chunks, oFFs, pCAL, sCAL and sTER; JPEG APP0 JFIF/JFXX, APP2 ICC_PROFILE, MPF and the ISO 21496-1 gain map, APP14 Adobe, the XMP an HDR decoder needs to find and apply a gain map (Ultra HDR `hdrgm` and `Container`/`Item` directory, Apple `apdi` and `HDRGainMap` version and headroom, and the motion-photo `Camera:MotionPhoto`, `MotionPhotoVersion` and `MotionPhotoPresentationTimestampUs` so a motion photo stays one; Clear writes a new packet from those properties alone, each checked against the value the format defines (a version, a Boolean, a Real or three, an integer, a timestamp that may be -1, an item semantic of `Primary`, `GainMap` or `MotionPhoto`, a MIME type of `image/jpeg`, `image/heic`, `image/avif`, `video/mp4` or `video/quicktime`, a length or padding), so no other property, attribute, nested structure or text survives; an item value outside those lists is dropped and the item keeps its place and length, a directory item may be written as `rdf:li` > `rdf:Description`, and a directory with an entry that holds no item is dropped whole; the first occurrence of a property wins, and the gain map's `Item:Length` follows its new length while later items such as a motion-photo video keep theirs. An Apple gain map of version 65536 (iPhone 12-14) has no headroom in its XMP: ImageIO and libultrahdr take it from the primary image's Apple maker note, tags 33 and 48. For such a file Clear writes a new Exif in the primary holding only those two maker-note values, copied as stored, and nothing else of the Exif: no IFD0, GPS or other Exif tag and no other maker-note tag. The values kept are the ones ImageIO applies, measured with every level duplicated and broken (where libultrahdr reads a file differently, Clear follows ImageIO): the first Exif segment, whose TIFF magic must be 42; at each level (IFD0's Exif IFD pointers, each Exif IFD's MakerNote entries, the maker note's 0x0021 and 0x0030 entries) an occurrence ImageIO cannot use is skipped and a later usable one overrides an earlier one, two Exif IFDs merging value by value; a maker-note value is usable when it is SRATIONAL with every value inside the maker note, at any offset; the headroom is kept only when both winning entries have count 1 (otherwise ImageIO renders no HDR image), and a winning MakerNote that is not Apple's keeps nothing. ImageIO then renders exactly what it rendered before, HDR or SDR; without the values it renders the photo SDR and libultrahdr refuses it. The new Exif is a minimal carrier for the two decode parameters, not a conformant Exif: it deliberately omits the tags Exif marks mandatory (ExifVersion, ComponentsConfiguration, ColorSpace, the pixel dimensions, YCbCrPositioning, the resolution tags), consistent with Clear removing all other Exif, so `exiftool -validate` warns about them; ImageIO, libultrahdr, ffmpeg, exiftool and TagLib# all read it), and the APP11 JPEG XT decoding boxes (`ftyp`, `TONE`, `RTRF`, `LTRF`, `LCHK`, `SPEC`, `RESI`); GIF graphic control and plain text extensions, looping and ICC application extensions. Everything else goes, including PNG text, `eXIf`, `tIME` and `caBX`, JPEG Exif (but for the Apple headroom above), the rest of XMP and extended XMP, IPTC, maker segments and every other APP11 box (JUMBF/C2PA, `xml `, `uuid`, `json`, `Exif`, unknown), and GIF XMP and ImageMagick's IPTC. In a multi-picture JPEG (MPF), Clear cleans every image the MPF index lists with the same rules; the index's offsets and sizes are rewritten after Clear and after an edit. An MP index outside its APP2 segment, or listing an image before the first image's end (its EOI), outside the file or overlapping another, is not followed: Clear is refused, and so is an edit that would move the later images. Apple's PNG `iDOT` is dropped when a chunk between it and the first IDAT changes. A field the format does not store reads back as null. EXIF, IPTC, and XMP values are not read yet, and SVG metadata is not read or written yet.

#### Raw AAC (ADTS/ADIF) Support
- `AacFile` reads and writes raw AAC files (`.aac`): ADTS (ISO/IEC 13818-7 §6.2, MPEG-2 and MPEG-4) and ADIF (§6.1)
- `AacAudioProperties`: duration from every ADTS frame header (1024 samples per raw data block), average bitrate, sample rate, channels (including a program config element when channel_configuration is 0), profile, MPEG version, and frame count. ADIF duration comes from the header bitrate
- `AacProfile` and `AacStreamFormat` enums, and `MediaFormat.Aac`
- ID3v2 in front, APEv2 and ID3v1 at the end, unified through `AacFile.Tag`. Saving rewrites only the tags and copies the AAC frames unchanged. An APEv2 tag is written with a header and a footer, except that a footer-only tag that was read keeps its layout
- `MediaFile` detection and `AacFile.Read` share one start rule. Without a tag, offset 0 is checked; after a leading ID3v2 tag, the next 8 KB. The audio ends at the first trailing tag (APEv2, a Lyrics3 v1 or v2 block in front of ID3v1, or ID3v1) or at the end of the file, and both end a frame the same way, so a trailing tag never changes the answer or the frame count. "ADIF" counts only at the first byte and only when its header parses inside the audio. An ADTS header (layer '00') counts when the next frame header inside the audio names the same stream, and at the first byte also when its frame runs to or past the end of the audio. An MPEG audio frame header that is confirmed, or whose frame runs past the audio, ends the search as MP3, so ID3-tagged MP3, even truncated, stays MP3. A start header cut by a trailing tag is not a header. `.aac` is an extension fallback

### Changed

- An Exif field edit (Comment, Description, Copyright and the other stored fields) runs only when the Exif structure is plain. Otherwise it is refused, with the reason, and nothing is written. Clear is unchanged. The copy-and-compare edit is gone: a removed value that shares a protected byte, or a compaction that would rewrite any protected range other than the table itself, is refused. A strip, tile or thumbnail offset or length that is not SHORT or LONG, an array of those that does not lie wholly in the data, an array of those named more than once, a pair tag given twice in one table, a complete pair that names more values than half the bytes, a classic next-IFD field with one, two or three of its four bytes, or a first entry whose format would change whether exiftool reads the table, each refuse on their own. A wholly absent next field stays editable. A first-entry change that shows nothing new is still refused.
- A path save copies still-image pixels in a rented 64 KB buffer and writes the same bytes as `Render`. `SaveToFile(path)` and `SaveToFileAsync` do this for JPEG, PNG, GIF, BMP, PCX, Netpbm, TIFF, DNG, and SVG. `Render` still copies every other byte.
- A path save copies the audio in a rented 64 KB buffer and writes the same bytes as `Render` (#20). `SaveToFile(path)` and `SaveToFileAsync` do this for Ogg Vorbis, Opus, and Ogg FLAC, MP3, native FLAC, ASF/WMA, raw AAC, MPEG program streams, Audible, Monkey's Audio, WavPack, Musepack, DSF (when the samples were left on disk), MP4 including a chapter edit, WAV, AIFF, AVI, Matroska, and DFF. `SaveToFile(path, span)`, `Render`, and `ReadFromFile` still build one buffer. The source is closed when the destination is never created. A render failure is returned before an already-cancelled token.
- Matroska: `TotalTracks` and `TotalDiscs` follow the tagging spec. `TOTAL_PARTS` counts the parts of the first lower level ([tagging](https://www.matroska.org/technical/tagging.html)), so at 50 it is the album's track count and at 60 the set's disc count ([audio examples](https://www.matroska.org/technical/tagging-audio-example.html), "Collection of CD sets"). `Track` is `PART_NUMBER` at 30 and `DiscNumber` is `PART_NUMBER` at 50. ffmpeg's untargeted `PART_NUMBER` (read as the track), its `4/12` and `DISC=2/3` slash forms and its `TRACKTOTAL`/`DISCTOTAL` names are read as fallbacks; setting a total writes `TOTAL_PARTS` and removes them. A `TOTAL_PARTS` at 30 is read as the track total only when nothing else gives one, and is never written.
- Matroska: TagLib writes the track total at 30 and the disc total at 50, against the spec. A TagLib-written file holding both totals now reads its disc total as `TotalTracks`, and TagLib reads a stored level 50 `TOTAL_PARTS` written here as its disc total, reads an untargeted one (what this library writes on ffmpeg files) as its track total, and does not read 60. This is a deliberate spec choice.
- Matroska: a new 60 or 50 tag goes before the file's narrower tags and before ffmpeg's untargeted tag, the order of the spec's examples, so readers that keep the last value they meet (MediaInfo) still show the track. New fields go into ffmpeg's untargeted tag, not into a disc-only tag. Setting `TotalDiscs` on a file with no 40 or 50 tag first moves a known disc number to 50.
- Matroska: a save fails when a picture is not an `image/` media type, has no data, would be added to a WebM file, or could not be referenced by the SeekHead. A save that does not set `Pictures` keeps the `Attachments` element byte for byte.
- Matroska: a save keeps the original `Tags` and `Chapters` bytes when those elements were not edited, and a save with no edits returns the file unchanged.
- Matroska: `Clear()` followed by a save now also removes the image attachments (cover art). Fonts and other non-image attachments are kept.
- Matroska: an attachment named `back.jpg`, `artist.png` and so on reads with that picture type instead of `Other`.
- Matroska: a new SeekHead entry is added only to the first SeekHead (RFC 9559 section 6.3).
- A file that starts with an ID3v2 tag followed by an ADTS or ADIF stream is now detected as `MediaFormat.Aac` instead of `MediaFormat.Mp3`, and `Mp3File.IsValidFormat` returns false for it
- APE: `ApeTagItem.Parse` and the item check that places an APEv2 tag (`ApeTagLocation`, used by WavPack, Musepack, Monkey's Audio and AAC) now share one set of item rules, and the check no longer copies each item value. The accepted tags are unchanged. The reserved-key error now names the reserved spelling ("ID3", "TAG", "OggS", "MP+"), not the key as written.
- Matroska `AlbumArtists` reads and writes a flat `ALBUM_ARTIST` (the name ffmpeg writes) when the file has one, so it pairs with that file's `ALBUM_ARTIST_SORT`; otherwise it is still the album-level `ARTIST`. Clearing it removes `ALBUM_ARTIST` and `ALBUM_ARTIST_SORT`.
- Matroska sort setters treat an empty string like null and remove the sort, the way the parent setters already do.
- Matroska fields written for the whole file (`Year`, `Comment`, `Genre`, `Genres`, `Composers` and the like) are written where their getter reads them. A value inherited from a broader target is replaced in place instead of shadowed by a copy at the track level, and a clear removes the name at every level the getter reads, so `Genre = null` no longer leaves an inherited genre showing. A new field goes beside the file's other fields, not into a Tag that holds only `ARTIST`.
- Matroska flat sorts (`ARTIST_SORT`, `ALBUM_ARTIST_SORT`, `COMPOSER_SORT`) belong to the value in their own Tag, as ffmpeg writes them. A Tag without that value passes its sort down to the value below it, and a broader Tag that repeats the same value is the same value (the tagging spec allows repeating it per track). Only a broader Tag with a different value keeps its own sort apart. A set leaves one copy, in the value's Tag; a clear removes every copy of that value's sort; a cleared value takes the sorts of its own Tag and of the value-less broader Tags above it, unless an equal value below still reads them, and a broader Tag that repeats it keeps its own; and a value placed between another value and its sort moves that sort down to the value it sorts. The track title, album title and album-level album artist pair their sorts over the run that starts at their level, so a value-less `ALBUM_ARTIST_SORT` at 60 or 70 is read, cleared and replaced too. A value written equal to the broader one it shadows repeats that SimpleTag with its `SORT_WITH`. Tags that target a chapter, attachment, track or edition are never read or changed by these rules.
- Matroska custom fields named `TITLE_SORT`, `ARTIST_SORT`, `ALBUM_SORT`, `ALBUM_ARTIST_SORT` or `COMPOSER_SORT` are read, written and removed through the matching sort property, so they pair with their value and see a nested `SORT_WITH`; a sort takes one value.
- Matroska setters treat writing back the value read as a no-op, so the file's bytes stay as they were; a null still clears, including a value a broader level holds behind an empty one. They follow `Tag.IsWriteBack`, checked before anything changes and against the list the getter reads: `""` or an empty list is a write-back only when no value holds text, and `CopyTo` makes the target hold exactly the source's value, so a copy equal to the target's first ISRC, catalog number, artist or genre replaces the rest of the list. `Publisher = null` also removes `LABEL`, and `ReplayGainTrackGain`/`ReplayGainTrackPeak` remove the older `REPLAYGAIN_GAIN`/`REPLAYGAIN_PEAK` copy on a set or clear, as their getters read those names.

### Fixed

- MP4 `Genre` reads the legacy `gnre` atom (a 1-based ID3v1 code: 25 is Soundtrack, 99 Easy Listening, 74 Acid Punk) when `©gen` is empty (#123). Setting `Genre` writes `©gen` and removes `gnre`.
- MP4 setters remove every atom their getter reads, so readers that show all of them (ffprobe joins them with `;`) show only the new value. Aliases read as a fallback and removed on set: `gnre` (Genre), `©cpy` (Copyright), `©com` (Composer) and `©swr` (EncoderSettings) from QuickTime user data, `titl`, `albm` and `dscp` (Title, Album, Description) as ExifTool reads them inside `ilst`, and `©st3` (Subtitle). `©aut`, `©inf`, `©des` and `grup` are not aliases and are kept. 3GPP's own `titl`/`albm`/`dscp` FullBoxes directly under `udta` are not read.
- MP4 saves keep `ilst` children the library does not map (for example `orch`, `©ope`, `©url`, `stik`, `purd` and the iTunes Store ids), a malformed cover or freeform atom, and freeform names that contain `:` (such as the CDDB `UFID…` atom of iTunes rips). Before, a save dropped them, even with no edit. The meta's other children (`hdlr`, `keys`, `free` padding) are written back as their original bytes.
- MP4 reads stay linear in the number of `ilst` children of any kind, including thousands of distinct freeform atoms.
- MP4 values keep their locale indicator when written back; new and edited values use 0. A parsed cover keeps its type code; a new picture is typed from its bytes (JPEG 13, PNG 14, GIF 12, BMP 27), then from its MIME type, and 0 only when neither identifies it.
- MP4 `RemovePictures()` removes only the covers; it used to clear every field.
- An MP4 property reads its atom even when the box parser does not list that atom as a container.
- Matroska: deeply nested SimpleTags no longer crash the process with a stack overflow (#150). `MatroskaFile.Read` parses SimpleTags up to 64 levels deep and keeps any deeper SimpleTag as its stored bytes, which a save writes back unchanged; a file with SimpleTags nested 100,000 deep used to end the process on open. A save that moves Cues already walks only the RFC 9559 §5.1.5 paths.
- `MediaFile.Read` (path or stream) takes the long detection peek whenever the first 32 bytes match no magic, even when the file name has a known extension, so a mislabeled file (an MP3 named `.avi`, `.wav` or `.mkv`) is found by its content as `MediaFile.ReadFromData` already did.
- Ogg Vorbis comment packets that run across pages without the continuation flag keep their comments, and a stream whose last audio page is cut off keeps its duration (#126). `missing_flag.ogg` from that issue now opens with its title, artist, album, genre, cover and 10.189 s duration. Its framing byte is 0x01: the comments were lost in page reassembly, not at the framing bit.
- The Vorbis comment framing bit is read as the spec defines it: the low bit of the first byte after the declared comment list ([v-comment.html](https://xiph.org/vorbis/doc/v-comment.html) steps 7-8, Vorbis I spec 5.2.1, bit order per 2.1.4). 0x01, 0x03, 0x81 and 0xFF are accepted, and padding after that byte is ignored. A framing byte of 0x00, any other byte with the low bit clear, or a packet that ends at the comment list is rejected, as in libvorbis and mutagen. All 60 Ogg Vorbis files in the test corpora use 0x01.
- Ogg Opus: OpusTags split across pages without the continuation flag keeps its tags (they were empty). When the last audio page is cut off, `MediaFile.Read` on a stream reports the duration instead of 0, for example `psycho4.opus` (244 s).
- Matroska: a track with `FlagEnabled` 0 is no longer chosen as the main audio or video track while an enabled track of that type exists (#117).
- AIFF and AIFF-C files return their native `ANNO`, `COMT` (marker id 0), `NAME`, `AUTH` and `(c) ` text through `Tag` when the ID3 tag has no value for that field (#127). Text is read as Mac OS Roman, or as UTF-8 when the whole value is valid UTF-8 with a multibyte sequence
- Editing Title, Artist, Comment, Copyright or Performers on an AIFF file writes the value to the ID3 chunk and removes that field's native chunk on save, even when the new value equals the old one. `COMT` notes attached to other markers are kept byte for byte. An unchanged save leaves the native chunks as they are
- Matroska: when a save inserts tags before an unknown-size Cluster, each Cluster `Position` at or past the insertion point moves with the data and the Cluster CRC-32 is recomputed (#118). `CueCodecState`, a Segment Position (RFC 9559 section 5.1.5.1.2.6), moves the same way. When the new value does not fit, the Cues are written again with a wider value (an unsigned integer may use up to 8 octets, RFC 8794 section 7.2); it is not voided, because a missing CueCodecState reads as 0, "the codec state is in the TrackEntry", which is a different value. A live `0` stays `0`, and a `Position` that no longer fits is voided, since it is optional. The walk is linear in the number of top-level elements.
- Matroska: `DiscNumber` and `TotalDiscs` are read and written (#130). Setting or clearing a track or disc number or total leaves the other three as they were, including a total held in a `4/12` or `2/3` slash, and a clear removes every element the getter could still read.
- Matroska: every rendered Tag has a `Targets` element. An omitted `TargetTypeValue` is not written back as 50, and an empty one reads as the default 50. A Tag that a clear leaves with no SimpleTag is not written (RFC 9559 section 5.1.8.1.2); one that was already empty when read is kept.
- Matroska: an edited or added top-level element stays listed in the SeekHead (RFC 9559 section 6.1). When the SeekHead has no room for its Seek, or its Seek cannot hold the new offset, the element goes right after the first SeekHead. The SeekHead is written again with every Seek at the width it needs, and everything after it moves once. Without a SeekHead the element goes before the first Cluster. Before this, an artist edit or a no-op save on `test1.mkv` or `test6.mkv` dropped the Tags Seek, and ffprobe and mediainfo lost the tags.
- Matroska: when a save moves elements, the Segment Positions (RFC 9559 section 16) in each SeekPosition in every SeekHead, the Cues `CueClusterPosition`, `CueCodecState` and the historic `CueRefCluster` and `CueRefCodecState`, and Cluster `Position` follow them, with their CRC-32 values. A value that no longer fits its field is written wider, and its SeekHead or Cues element grows, moving what follows once more; however the widths cascade, the work stays linear in the size of the SeekHead and Cues elements. The deprecated `ReferenceVirtual` (Appendix A.4, inside a BlockGroup) is left as it is. Before this, a cover or Tags element over about 64 KB on a file whose CueClusterPosition had two bytes was refused or lost its Seek, a CueCodecState kept its old value, and growing an element could void another element's Seek. A zero CueCodecState still names the TrackEntry. An empty Cues position reads as 0 (RFC 8794 section 7.2) and is kept, or written wider when the offset 0 it names moves. A Cluster `Position` that cannot widen in place is voided, since it is optional. Cues values are read only on their RFC 9559 section 5.1.5 paths (Cues, CuePoint, CueTrackPositions, CueReference); any other nesting is copied as it is, so a file that nests CuePoints thousands deep cannot exhaust the stack. The widths are rewritten at most seven times; a crafted file whose widths keep growing is refused there (a move falls back to its first placement).
- Matroska: a save keeps one copy of the Segment body at a time; a move no longer holds a second one.
- **MP4**: A version 1 `mvhd` or `mdhd` whose 64-bit duration is past `TimeSpan.MaxValue` no longer throws
  out of `Mp4File.Read` or `Mp4AudioPropertiesParser.Parse`; the duration saturates.
- ID3v2 UTF-16 text with a byte order mark on each string (ID3v2.3 §3.3, mutagen's default) reads without a
  U+FEFF at the start of every value after the first, in text frames, `TXXX`, and `TIPL`/`TMCL`/`IPLS`. One
  decoder reads all of them on every target framework. A text or `TXXX` frame read from a file renders with the
  BOMs and the trailing terminators it was read with, so an empty last value survives an unchanged save. A
  `TXXX` that `SetCustomField` writes in an ID3v2.3 tag has a BOM before each value; a frame built in code keeps
  one BOM, as before.
- Writing back the gain `R128TrackGainDb` or `R128AlbumGainDb` read keeps the stored text, so `-0512` is not
  rewritten as `-512`. `null` still clears it, text the getter cannot parse included.
- An ASF title, artist, copyright, comment, or rating write replaces the same-named Extended Content Description
  descriptor that ffmpeg writes beside the Content Description field, so other tools no longer show the old value.
  An empty Content Description field reads that descriptor.

### Changed

- An APE artist, album artist, composer, or genre is the first name. It is not a comma-joined string. A Basic copy from APE into MP4, ASF, RIFF INFO, or ID3v1 keeps that first name. A copy with the Extended options into ASF keeps every name, the first in the Content Description and the rest in the Metadata Library. A copy into ID3v2 keeps the full list.
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
