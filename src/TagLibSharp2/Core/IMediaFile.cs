namespace TagLibSharp2.Core;

/// <summary>
/// Represents a media file that can be read, modified, and saved.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides a common abstraction for all media file types,
/// enabling polymorphic code that works with any supported format.
/// </para>
/// <para>
/// All file classes (FlacFile, Mp3File, Mp4File, etc.) implement this interface.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Work with any media file type
/// IMediaFile file = result.File!;
/// Console.WriteLine($"Title: {file.Tag?.Title}");
/// Console.WriteLine($"Duration: {file.AudioProperties?.Duration}");
/// Console.WriteLine($"Format: {file.Format}");
/// </code>
/// </example>
public interface IMediaFile : IDisposable
{
	/// <summary>
	/// Gets the metadata tag for this file.
	/// </summary>
	/// <remarks>
	/// The returned tag type depends on the file format. For example:
	/// <list type="bullet">
	/// <item>FLAC files return VorbisComment</item>
	/// <item>MP3 files return a combined tag (Id3v2Tag preferred, Id3v1Tag fallback)</item>
	/// <item>MP4 files return Mp4Tag</item>
	/// </list>
	/// </remarks>
	Tag? Tag { get; }

	/// <summary>
	/// Gets the audio properties (duration, bitrate, sample rate, channels) for this file.
	/// </summary>
	/// <remarks>
	/// May be null if the file format doesn't have audio properties or if parsing failed.
	/// </remarks>
	IMediaProperties? AudioProperties { get; }

	/// <summary>
	/// Gets the path this file was read from, if applicable.
	/// </summary>
	/// <remarks>
	/// This is only set when the file was loaded using ReadFromFile or ReadFromFileAsync.
	/// Files parsed from byte data will have null SourcePath.
	/// </remarks>
	string? SourcePath { get; }

	/// <summary>
	/// Gets the detected format of this media file.
	/// </summary>
	MediaFormat Format { get; }
}
