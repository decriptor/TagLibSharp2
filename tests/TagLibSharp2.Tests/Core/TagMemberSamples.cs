// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Reflection;
using TagLibSharp2.Core;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A sample value for every settable <see cref="Tag"/> member, for tests that walk the whole
/// tag surface so a member added later is covered without editing the test.
/// </summary>
static class TagMemberSamples
{
	// Values that ID3 stores verbatim. Anything else gets a generic sample by type.
	static readonly Dictionary<string, object> Special = new () {
		["Year"] = "2021",
		["DateTagged"] = "2021-03-04",
		["OriginalReleaseDate"] = "2019-05-06",
		["Isrc"] = "USRC17607839",
		["Language"] = "eng",
		["Track"] = 3u,
		["TotalTracks"] = 9u,
		["DiscNumber"] = 1u,
		["TotalDiscs"] = 2u,
		["BeatsPerMinute"] = 120u,
		["R128TrackGain"] = "-512",
		["R128AlbumGain"] = "-256",
		["ReplayGainTrackGain"] = "-6.50 dB",
		["ReplayGainAlbumGain"] = "-7.25 dB",
		["ReplayGainTrackPeak"] = "0.988547",
		["ReplayGainAlbumPeak"] = "0.999969",
		["MusicBrainzTrackId"] = "b1a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzReleaseId"] = "c1a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzArtistId"] = "d1a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzReleaseGroupId"] = "e1a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzAlbumArtistId"] = "f1a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzRecordingId"] = "01a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzWorkId"] = "11a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzReleaseArtistId"] = "21a9c0e9-d987-4042-ae91-78d6a3267d69",
		["AcoustIdId"] = "31a9c0e9-d987-4042-ae91-78d6a3267d69",
		["MusicBrainzReleaseCountry"] = "GB",
		["MusicBrainzReleaseStatus"] = "official",
		["MusicBrainzReleaseType"] = "album",
		["PodcastFeedUrl"] = "https://example.com/feed",
	};

	/// <summary>Every public settable instance property of <see cref="Tag"/>, by name.</summary>
	public static IReadOnlyList<PropertyInfo> Settable { get; } =
		[.. typeof (Tag).GetProperties (BindingFlags.Public | BindingFlags.Instance)
			.Where (p => p.GetSetMethod () is not null)
			.OrderBy (p => p.Name, StringComparer.Ordinal)];

	public static object Sample (PropertyInfo p)
	{
		if (Special.TryGetValue (p.Name, out var value))
			return value;
		var type = Nullable.GetUnderlyingType (p.PropertyType) ?? p.PropertyType;
		if (type == typeof (string))
			return "v-" + p.Name;
		if (type == typeof (uint))
			return 7u;
		if (type == typeof (double))
			return 0.5;
		if (type == typeof (bool))
			return true;
		if (type == typeof (string[]))
			return new[] { p.Name + "1", p.Name + "2" };
		if (type == typeof (IPicture[]))
			return new IPicture[] { new PictureFrame ("image/png", PictureType.FrontCover, "", new BinaryData ([0x89, 0x50, 0x4E, 0x47, 1, 2])) };
		throw new AssertFailedException ($"No sample for {p.Name} ({p.PropertyType.Name}); add one.");
	}

	public static object? Cleared (PropertyInfo p)
	{
		if (p.PropertyType == typeof (bool))
			return false;
		if (p.PropertyType == typeof (string[]))
			return Array.Empty<string> ();
		if (p.PropertyType == typeof (IPicture[]))
			return Array.Empty<IPicture> ();
		return null;
	}

	/// <summary>A comparable text form of a member's value; null tag reads as unset.</summary>
	public static string Describe (PropertyInfo p, Tag? tag) => Describe (tag is null ? null : p.GetValue (tag));

	static string Describe (object? value) => value switch {
		null => "<null>",
		string s => "\"" + s + "\"",
		string[] a => "[" + string.Join (",", a) + "]",
		IPicture[] pictures => "pictures:" + string.Join (",", pictures.Select (x => x.MimeType + "/" + x.PictureData.Length)),
		_ => value.ToString () ?? "?"
	};
}
