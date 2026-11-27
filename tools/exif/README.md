# How exiftool reads Exif tables

The Exif editor removes a field's copies from every table exiftool reads with its Exif tags, and the tests' reader
model (`tests/TagLibSharp2.Tests/Image/ExifReaderModel.cs`) is the oracle for what that means. Each rule below
was measured with exiftool 13.55 (`exiftool -a -G1 -H -s -U -EXIF:All` on the TIFF wrapped in a JPEG APP1). The
probe column names the input in `Golden/exif-reader-probes.txt` that records it, as `ExifReaderGoldenTests.Probes`
names it, and the result is the field-tag counts exiftool showed (`-` for none). In the probes, `E9286` is a
UserComment and `E010E` an ImageDescription in the pointed-at table, and `G0001` is GPSLatitudeRef in a GPS table.

`golden.sh` regenerates both golden files: it has the tests write each input, runs exiftool over them with
`golden.cs`, and writes the counts as text. No media is committed. `ReaderModel_ShowsWhatExiftoolShows_ForEveryProbe`
and `..._ForEveryMatrixInput` fail when the model and exiftool differ on any input.

## Which tables are read

| rule | probe | exiftool |
|------|-------|----------|
| IFD0 and its next chain, and the Exif (0x8769), SubIFD (0x014A) and interoperability (0xA005) IFDs any of them names, are read with Exif tags and their pointers followed | `pointer 8769 in IFD0 type 4 count 1 LE`, `... in ExifIFD ...`, `... in InteropIFD ...`, `... in SubIFD ...`, `... in IFD1 ...` | `E010E=1 E9286=1` each |
| A GPS IFD (0x8825) is read with GPS tags | `pointer 8825 in IFD0 type 4 count 1 LE` | `G0001=1` |
| Nothing inside a GPS IFD is followed | `pointer 8769 in GPS type 4 count 1 LE` (and `A005`, `8825`, `014A`) | `-` |
| The next IFD of an Exif, interoperability, SubIFD or GPS table is not read; IFD1's is | `next IFD of ExifIFD`, `... InteropIFD`, `... SubIFD`, `... GPS`, `... IFD1` | `-`, `-`, `-`, `G0001=1` (the GPS table only), `E9286=1` |
| A classic table is read when 0, 2 or 4 bytes of its next-IFD field are present, and dropped when 1 or 3 are | measured for IFD0, IFD1 and the Exif, GPS and interoperability IFDs (exiftool 13.55); size pinned by the TIFF size tests | read for 0, 2 and 4; dropped for 1 and 3 |

The field editor is stricter than that read. It refuses a field edit when 1, 2 or 3 bytes of a classic next-IFD field are present, and it still edits when the next field is wholly absent. `Clear` does not use this rule. A strip, tile or thumbnail array that does not lie wholly in the data is refused on its own, before the value budget. A first entry whose format would change whether exiftool reads the table is refused even when the change shows no new tag.

## How a pointer is decoded

| rule | probe | exiftool |
|------|-------|----------|
| Followed types: BYTE, SHORT, LONG, SSHORT, SLONG, IFD, and UNDEFINED with count 1 | `pointer 8769 in IFD0 type T count 1 LE` for T in 0-11, 13, 16 | read for 1, 3, 4, 7, 8, 9, 13; `-` for 0, 2, 5, 6 (value 200 is negative as SBYTE), 10, 11, 16 |
| SBYTE is followed when its value is not negative | `pointer 8769 SBYTE 100 LE` | `E010E=1 E9286=1` |
| An Exif IFD pointer needs count 1 | `pointer 8769 type 3 values 2 LE`, `pointer 8769 LONG array of 2 LE` | `-` |
| A GPS or interoperability pointer uses its first value only | `pointer A005 type 3 values 3 LE`, `pointer 8825 LONG array of 2 LE` | `E9286=1`, `G0001=1` |
| SubIFDs use every value, inline or out of line | `pointer 014A type 3 values 3 LE`, `pointer 014A LONG array of 2 LE` | `E9286=3`, `E9286=2` |
| UNDEFINED with count 2 names nothing | `pointer 014A in IFD0 type 7 count 2 LE` | `-` |
| Negative, zero, header and end-of-data offsets name nothing | `pointer value SBYTE negative`, `... SSHORT negative`, `... SLONG negative`, `... LONG 0`, `... SHORT 0`, `... LONG into the header`, `... LONG at the data's end` | `-` |
| An out-of-line SubIFD array inside its own table names nothing | `a SubIFD array inside its own table`, `a SubIFD array after its own table` | `-`, `E9286=1` |

| Two pointer arrays at one offset, one count, different types, are each read by their type | pinned by `ExifEditTests.Edit_TwoPointerArraysAtOneOffset_AreEachReadByTheirOwnType` | both |

## The order tables are read in, and tables reached twice

| rule | probe | exiftool |
|------|-------|----------|
| Depth first, in entry order; IFD0's next IFD after IFD0's subdirectories | `Exif in depth before IFD0's GPS`, `IFD0's GPS before IFD1's Exif` | `E9286=1`, `G0001=1` |
| A table is read once, in the first kind it is reached as | `GPS first, then Exif`, `Exif first, then GPS`, `IFD1 is the Exif IFD`, `the Exif IFD is IFD0`, and every `<kind>, then <kind>` pair | the first kind's tags only |
| ... except that a table first read as interoperability is read again as GPS | `Interop, then GPS` | `E9286=1 G0001=1` |
| An interoperability IFD's own pointers are followed | `an Interop IFD's Interop and Exif IFDs` | `E9286=2` |

## Entries and values

| rule | probe | exiftool |
|------|-------|----------|
| An entry of type 0 or 14 and above is skipped | `type 0 in the middle`, `type 19 last`, `type 255 in the middle` | the other entries are read |
| A table whose first entry has such a type is dropped whole | `type 0 first`, `type 14 first` ... `type 255 first` | `-` |
| The same drop, for a first entry of type 0 or 14, in IFD0, IFD1, an Exif IFD, a SubIFD, an interoperability IFD and a GPS IFD. A later type-14 entry in each is skipped. Both byte orders do this (the probes are little-endian; the editor tests pin both) | `IFD0 type 0 first`, `IFD0 type 14 first`, `IFD0 type 14 later`, and the same three for `IFD1`, `Exif`, `SubIFD`, `Interop` and `GPS` | `-` when the bad format is first; `E010E=1`, or `G0001=1` for GPS, when it is later |
| An empty IFD0 is read and its next IFD is followed. An IFD0 whose first entry is type 0 is dropped and its next IFD is not | `the chain after an empty IFD0`, `the chain after an IFD0 whose first entry is type 0` | `E010E=1`, `-` |
| A dropped main table ends the chain | `the chain after a table dropped for its first entry` | `-` |
| An out-of-line value is hidden when its offset is under 8 | `value of 8 at 0 by IFD0 at 100` ... `at 7`, `value of 8 at 8 by IFD0 at 100` | `-`, `E9286=1` |
| ... or when it overlaps its own table's count and entries (not the next offset) | `value at 41 by an Exif IFD at 40 to 70`, `... at 65`, `... at 66`, `... at 70` | `-`, `-`, `E9286=1`, `E9286=1` |
| ... or when it runs past the data | `value at 395 by an Exif IFD at 40 to 70` | `-` |
| A value inside another table is shown | `value inside the parent table`, `value inside a child table` | `E9286=1` |
| A table is read only up to its 11th warning (an unknown format other than 0, a value past the data, a suspicious value; counted per table), when entries follow it | `10 unknown formats before the UserComment`, `11 unknown formats ...`, `11 values past the data ...`, `11 values in the header ...`, `11 values inside their own table ...` | `E9286=1`, then `-` for each 11 |
| Type 0 entries and tags out of sequence do not count | `11 entries of type 0 ...`, `11 tags out of sequence ...` | `E9286=1` |
| The count is per table | `6 warnings in IFD0 and 6 in the Exif IFD` | `E9286=1` |
| A stopped IFD0 follows no later pointer and no next IFD; an 11th warning on the last entry stops nothing | `IFD0 stopped before its Exif IFD pointer`, `IFD0 stopped, then its next IFD`, `IFD0's 11th warning on its last entry, then its next IFD` | `-`, `-`, `E9286=1` |

## Other readers

Measured on the same probes wrapped in JPEG (`TagLib.File.Create` 2.3.0 run as a black box; ImageIO's
`CGImageSourceCopyPropertiesAtIndex`, which needs a decodable JPEG, so for it each probe replaced the Exif of a real
photo); not in the golden files, since neither tool is part of this repository.

| rule | measured on | result |
|------|-------------|--------|
| TagLib# reads IFD0's 0x8769 and 0x8825 as the 4-byte field taken as a LONG, whatever the type and count | every `pointer 8769 in IFD0 type T count C` probe, and GPS probes of each type and count 0-2 | it follows type 0, 2, 5, 10, 11, 16 and count 0 or 2 when that LONG names a table, and no SHORT whose LONG does not |
| TagLib# follows no Exif or GPS pointer outside IFD0 | the `in ExifIFD`, `in InteropIFD`, `in SubIFD`, `in IFD1` probes | not followed |
| TagLib# reads a UserComment of a type exiftool skips | `a UserComment of type 0`, `... 12`, `... 14` to `... 19`, `... 255`, `type 14 first` ... | shown (not types 1 or 13); ImageIO shows only type 7 |
| ImageIO shows a UserComment exiftool does not only from a table exiftool drops for its first entry's format, or reads as GPS first and Exif after; every reader view used here reads both | the 1,656 pointer, value, format and order probes | 43 shown by both, 10 by ImageIO only (`type 0 first` ... `type 255 first`, `GPS first, then Exif`, `GPS, then Exif`), 318 by exiftool only |
| No reader reads the next IFD of an Exif IFD | an Exif IFD whose next IFD holds DateTimeOriginal and a UserComment | none of exiftool, TagLib#, ImageIO |
