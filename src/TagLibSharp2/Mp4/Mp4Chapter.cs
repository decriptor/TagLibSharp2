// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Core;

namespace TagLibSharp2.Mp4;

/// <summary>
/// One chapter of an MP4 file: a start time and a title.
/// </summary>
/// <remarks>
/// <para>
/// An MP4 file stores chapters in two places, and <see cref="Mp4File.Chapters"/> reads and writes both:
/// </para>
/// <list type="bullet">
/// <item>A QuickTime chapter track: a text track that another track names in its
/// <c>tref</c>/<c>chap</c> reference. Each sample is one chapter title, timed by the sample table
/// (ISO 14496-12 §8.3.3, §8.6, §8.7; QuickTime File Format, "Track Reference Atoms").</item>
/// <item>The Nero <c>chpl</c> box in <c>moov/udta</c>: start times in 100 ns units and titles of up to 255
/// UTF-8 bytes, at most 255 entries.</item>
/// </list>
/// <para>
/// Chapters have no end field. <see cref="EndTime"/> is the next chapter's start, or the file duration for the last.
/// </para>
/// </remarks>
public sealed class Mp4Chapter : IEquatable<Mp4Chapter>
{
	/// <summary>
	/// Creates a chapter.
	/// </summary>
	/// <param name="startTime">The chapter start, from the beginning of the presentation.</param>
	/// <param name="title">The chapter title.</param>
	/// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="startTime"/> is negative.</exception>
	public Mp4Chapter (TimeSpan startTime, string title)
	{
#if NETSTANDARD2_0 || NETSTANDARD2_1
		Polyfills.ThrowIfNull (title, nameof (title));
#else
		ArgumentNullException.ThrowIfNull (title);
#endif
		if (startTime < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (startTime), "A chapter cannot start before zero.");

		StartTime = startTime;
		Title = title;
	}

	Mp4Chapter (TimeSpan startTime, string title, TimeSpan? endTime)
	{
		StartTime = startTime;
		Title = title;
		EndTime = endTime;
	}

	/// <summary>
	/// Gets the chapter start, from the beginning of the presentation.
	/// </summary>
	public TimeSpan StartTime { get; }

	/// <summary>
	/// Gets the chapter title.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Gets the chapter end: the next chapter's start, or the file duration for the last chapter.
	/// </summary>
	/// <remarks>
	/// Null on a chapter created with the constructor. A chapter returned by <see cref="Mp4File.Chapters"/>
	/// always has an end time.
	/// </remarks>
	public TimeSpan? EndTime { get; }

	internal Mp4Chapter WithEndTime (TimeSpan endTime) =>
		new (StartTime, Title, endTime < StartTime ? StartTime : endTime);

	/// <summary>
	/// True when both chapters have the same start time and title. <see cref="EndTime"/> is derived and is not compared.
	/// </summary>
	/// <param name="other">The chapter to compare.</param>
	/// <returns>True when start time and title match.</returns>
	public bool Equals (Mp4Chapter? other) =>
		other is not null && StartTime == other.StartTime && string.Equals (Title, other.Title, StringComparison.Ordinal);

	/// <inheritdoc/>
	public override bool Equals (object? obj) => obj is Mp4Chapter other && Equals (other);

	/// <inheritdoc/>
	public override int GetHashCode () => HashCode.Combine (StartTime, StringComparer.Ordinal.GetHashCode (Title));

	/// <inheritdoc/>
	public override string ToString () => $"{StartTime} {Title}";
}
