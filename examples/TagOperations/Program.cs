// TagLibSharp2 Tag Operations Examples
// Demonstrates reading, writing, and modifying audio metadata

using TagLibSharp2.Core;
using TagLibSharp2.Id3;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Xiph;
using TagLibSharp2.Ogg;

Console.WriteLine ("=== TagLibSharp2 Tag Operations Examples ===\n");

// ============================================================
// 1. Creating ID3v2 Tags from Scratch
// ============================================================
Console.WriteLine ("1. Creating ID3v2 Tags from Scratch");
Console.WriteLine (new string ('-', 50));

// Create a new ID3v2.4 tag with basic metadata
var tag = new Id3v2Tag (Id3v2Version.V24) {
	Title = "My Song",
	Artist = "The Band",
	Album = "Greatest Hits",
	Year = "2024",
	Track = 5,
	Genre = "Rock"
};

Console.WriteLine ($"Title:  {tag.Title}");
Console.WriteLine ($"Artist: {tag.Artist}");
Console.WriteLine ($"Album:  {tag.Album}");
Console.WriteLine ($"Year:   {tag.Year}");
Console.WriteLine ($"Track:  {tag.Track}");
Console.WriteLine ($"Genre:  {tag.Genre}");

Console.WriteLine ();

// ============================================================
// 2. Extended Metadata Properties
// ============================================================
Console.WriteLine ("2. Extended Metadata Properties");
Console.WriteLine (new string ('-', 50));

// Set extended properties
tag.AlbumArtist = "Various Artists";
tag.Composer = "John Smith";
tag.Conductor = "Jane Doe";       // TPE3 frame
tag.Copyright = "2024 Record Co."; // TCOP frame
tag.DiscNumber = 1;
tag.TotalDiscs = 2;
tag.TotalTracks = 12;
tag.BeatsPerMinute = 120;
tag.IsCompilation = true;         // TCMP frame

Console.WriteLine ($"Album Artist:     {tag.AlbumArtist}");
Console.WriteLine ($"Composer:         {tag.Composer}");
Console.WriteLine ($"Conductor:        {tag.Conductor}");
Console.WriteLine ($"Copyright:        {tag.Copyright}");
Console.WriteLine ($"Disc:             {tag.DiscNumber}/{tag.TotalDiscs}");
Console.WriteLine ($"Track:            {tag.Track}/{tag.TotalTracks}");
Console.WriteLine ($"BPM:              {tag.BeatsPerMinute}");
Console.WriteLine ($"Is Compilation:   {tag.IsCompilation}");

Console.WriteLine ();

// ============================================================
// 3. Lyrics (USLT Frame)
// ============================================================
Console.WriteLine ("3. Lyrics (USLT Frame)");
Console.WriteLine (new string ('-', 50));

// Simple lyrics (uses default English language)
tag.Lyrics = "Verse 1:\nThis is the first verse.\n\nChorus:\nThis is the chorus.";

Console.WriteLine ($"Lyrics: {tag.Lyrics?.Substring (0, Math.Min (50, tag.Lyrics?.Length ?? 0))}...");

// Multiple lyrics in different languages
tag.AddLyrics (new LyricsFrame ("German lyrics here", "deu", ""));
tag.AddLyrics (new LyricsFrame ("French lyrics here", "fra", ""));

Console.WriteLine ($"Total lyrics frames: {tag.LyricsFrames.Count}");

// Find lyrics by language
var germanLyrics = tag.GetLyricsFrame (language: "deu");
Console.WriteLine ($"German lyrics found: {germanLyrics is not null}");

Console.WriteLine ();

// ============================================================
// 4. MusicBrainz IDs
// ============================================================
Console.WriteLine ("4. MusicBrainz IDs");
Console.WriteLine (new string ('-', 50));

// TXXX-based MusicBrainz IDs
tag.MusicBrainzTrackId = "550e8400-e29b-41d4-a716-446655440000";
tag.MusicBrainzReleaseId = "f47ac10b-58cc-4372-a567-0e02b2c3d479";
tag.MusicBrainzArtistId = "6b8a7b1a-2e7b-4c3d-8f9e-0a1b2c3d4e5f";

// UFID-based MusicBrainz Recording ID (canonical format)
tag.MusicBrainzRecordingId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

Console.WriteLine ($"Track ID (TXXX):     {tag.MusicBrainzTrackId}");
Console.WriteLine ($"Release ID (TXXX):   {tag.MusicBrainzReleaseId}");
Console.WriteLine ($"Artist ID (TXXX):    {tag.MusicBrainzArtistId}");
Console.WriteLine ($"Recording ID (UFID): {tag.MusicBrainzRecordingId}");

// Access the UFID frame directly
var ufidFrame = tag.GetUniqueFileId (UniqueFileIdFrame.MusicBrainzOwner);
Console.WriteLine ($"UFID Owner: {ufidFrame?.Owner}");

Console.WriteLine ();

// ============================================================
// 5. ReplayGain Tags
// ============================================================
Console.WriteLine ("5. ReplayGain Tags");
Console.WriteLine (new string ('-', 50));

tag.ReplayGainTrackGain = "-6.5 dB";
tag.ReplayGainTrackPeak = "0.987654";
tag.ReplayGainAlbumGain = "-5.2 dB";
tag.ReplayGainAlbumPeak = "0.999999";

Console.WriteLine ($"Track Gain: {tag.ReplayGainTrackGain}");
Console.WriteLine ($"Track Peak: {tag.ReplayGainTrackPeak}");
Console.WriteLine ($"Album Gain: {tag.ReplayGainAlbumGain}");
Console.WriteLine ($"Album Peak: {tag.ReplayGainAlbumPeak}");

Console.WriteLine ();

// ============================================================
// 6. Comments (COMM Frame)
// ============================================================
Console.WriteLine ("6. Comments (COMM Frame)");
Console.WriteLine (new string ('-', 50));

// Simple comment
tag.Comment = "This is a great song!";
Console.WriteLine ($"Comment: {tag.Comment}");

// Multiple comments with descriptions
tag.AddComment (new CommentFrame ("Recording notes here", "eng", "Recording"));
tag.AddComment (new CommentFrame ("Mixing notes here", "eng", "Mixing"));

Console.WriteLine ($"Total comment frames: {tag.Comments.Count}");

// Find specific comment
var recordingComment = tag.GetCommentFrame (description: "Recording");
Console.WriteLine ($"Recording comment found: {recordingComment is not null}");

Console.WriteLine ();

// ============================================================
// 7. User-Defined Text (TXXX Frames)
// ============================================================
Console.WriteLine ("7. User-Defined Text (TXXX Frames)");
Console.WriteLine (new string ('-', 50));

// Custom metadata using TXXX frames
tag.SetUserText ("ENCODER", "TagLibSharp2 Example");
tag.SetUserText ("ORIGINAL_YEAR", "1985");
tag.SetUserText ("MOOD", "Energetic");

Console.WriteLine ($"Encoder:       {tag.GetUserText ("ENCODER")}");
Console.WriteLine ($"Original Year: {tag.GetUserText ("ORIGINAL_YEAR")}");
Console.WriteLine ($"Mood:          {tag.GetUserText ("MOOD")}");

Console.WriteLine ();

// ============================================================
// 8. Rendering and Parsing Tags
// ============================================================
Console.WriteLine ("8. Rendering and Parsing Tags");
Console.WriteLine (new string ('-', 50));

// Render tag to binary data
var rendered = tag.Render ();
Console.WriteLine ($"Rendered tag size: {rendered.Length} bytes");

// Parse it back
var parseResult = Id3v2Tag.Read (rendered.Span);
if (parseResult.IsSuccess) {
	var parsed = parseResult.Tag!;
	Console.WriteLine ($"Parsed Title:    {parsed.Title}");
	Console.WriteLine ($"Parsed Artist:   {parsed.Artist}");
	Console.WriteLine ($"Parsed Lyrics:   {(parsed.Lyrics is not null ? "Present" : "Missing")}");
	Console.WriteLine ($"Parsed UFID:     {(parsed.UniqueFileIdFrames.Count > 0 ? "Present" : "Missing")}");
}

Console.WriteLine ();

// ============================================================
// 9. Vorbis Comments (FLAC/Ogg)
// ============================================================
Console.WriteLine ("9. Vorbis Comments (FLAC/Ogg)");
Console.WriteLine (new string ('-', 50));

// Create Vorbis comments for FLAC/Ogg files
var vorbis = new VorbisComment {
	Title = "Vorbis Song",
	Artist = "Vorbis Artist",
	Album = "Vorbis Album",
	Track = 3,
	TotalTracks = 10,
	DiscNumber = 1,
	TotalDiscs = 1,
	Composer = "Vorbis Composer",
	Conductor = "Vorbis Conductor",
	Copyright = "2024 Vorbis Co.",
	IsCompilation = false,
	Lyrics = "Vorbis lyrics here..."
};

// ReplayGain and MusicBrainz IDs work the same way
vorbis.ReplayGainTrackGain = "-4.5 dB";
vorbis.MusicBrainzTrackId = "abc12345-def6-7890-abcd-ef1234567890";

Console.WriteLine ($"Title:      {vorbis.Title}");
Console.WriteLine ($"Artist:     {vorbis.Artist}");
Console.WriteLine ($"Track:      {vorbis.Track}/{vorbis.TotalTracks}");
Console.WriteLine ($"Conductor:  {vorbis.Conductor}");
Console.WriteLine ($"Has Lyrics: {vorbis.Lyrics is not null}");

// Access raw fields
Console.WriteLine ($"Raw ARTIST: {vorbis.GetValue ("ARTIST")}");

Console.WriteLine ();

// ============================================================
// 10. ID3v1 Tags
// ============================================================
Console.WriteLine ("10. ID3v1 Tags");
Console.WriteLine (new string ('-', 50));

// Create ID3v1 tag (limited to 30 chars per field)
var id3v1 = new Id3v1Tag {
	Title = "Short Title",
	Artist = "Short Artist",
	Album = "Short Album",
	Year = "2024",
	Track = 5,
	Genre = "Rock"
};

Console.WriteLine ($"ID3v1 Title:  {id3v1.Title}");
Console.WriteLine ($"ID3v1 Artist: {id3v1.Artist}");
Console.WriteLine ($"ID3v1 Track:  {id3v1.Track}");

// Render to 128 bytes (ID3v1 fixed size)
var id3v1Rendered = id3v1.Render ();
Console.WriteLine ($"ID3v1 size: {id3v1Rendered.Length} bytes (always 128)");

Console.WriteLine ();
Console.WriteLine ("=== Examples Complete ===");
Console.WriteLine ();
Console.WriteLine ("Note: To work with actual files, use:");
Console.WriteLine ("  - Mp3File.ReadFromFile(path) for MP3 files");
Console.WriteLine ("  - FlacFile.ReadFromFile(path) for FLAC files");
Console.WriteLine ("  - OggVorbisFile.ReadFromFile(path) for Ogg Vorbis files");
