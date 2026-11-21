// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Aiff;
using TagLibSharp2.Ape;
using TagLibSharp2.Asf;
using TagLibSharp2.Core;
using TagLibSharp2.Dff;
using TagLibSharp2.Dsf;
using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Mp4;
using TagLibSharp2.Mpeg;
using TagLibSharp2.Musepack;
using TagLibSharp2.Ogg;
using TagLibSharp2.Riff;
using TagLibSharp2.Tests.Asf;
using TagLibSharp2.WavPack;
using TagLibSharp2.Xiph;

namespace TagLibSharp2.Tests.Core;

/// <summary>
/// A custom field set through <see cref="IMediaFile.Tag"/> survives a save and a reopen in every container.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Core")]
public class CustomFieldFileRoundTripTests
{
	const string Name = "MY_CUSTOM_FIELD";

	/// <summary>One container: build a file, read it, render it with its changes.</summary>
	sealed record Container (
		Func<byte[]> Build,
		Func<byte[], IMediaFile> Read,
		Func<IMediaFile, byte[], byte[]> Render,
		bool MultiValue = true,
		string Name = Name);

	static readonly Dictionary<string, Container> Containers = new () {
		["Flac"] = new (
			() => TestBuilders.Flac.CreateWithVorbisComment ("t"),
			d => FlacFile.Read (d).File!,
			(f, o) => ((FlacFile)f).Render (o).ToArray ()),
		["Mp3"] = new (
			CreateMinimalMp3,
			d => Mp3File.Read (d).File!,
			(f, o) => ((Mp3File)f).Render (o).ToArray ()),
		["OggVorbis"] = new (
			() => TestBuilders.Ogg.CreateMinimalFile (),
			d => OggVorbisFile.Read (d).File!,
			(f, o) => ((OggVorbisFile)f).Render (o).ToArray ()),
		["Opus"] = new (
			() => TestBuilders.Opus.CreateMinimalFile (),
			d => OggOpusFile.Read (d).File!,
			(f, o) => ((OggOpusFile)f).Render (o).ToArray ()),
		["OggFlac"] = new (
			() => TestBuilders.OggFlac.CreateWithMetadata ("t"),
			d => OggFlacFile.Read (d).File!,
			(f, o) => ((OggFlacFile)f).Render (o)),
		["MonkeysAudio"] = new (
			() => TestBuilders.MonkeysAudio.CreateWithMetadata ("t"),
			d => MonkeysAudioFile.Read (d).File!,
			(f, o) => ((MonkeysAudioFile)f).Render (o)),
		["WavPack"] = new (
			() => TestBuilders.WavPack.CreateWithMetadata ("t"),
			d => WavPackFile.Read (d).File!,
			(f, o) => ((WavPackFile)f).Render (o)),
		["Musepack"] = new (
			() => TestBuilders.Musepack.CreateWithMetadata ("t"),
			d => MusepackFile.Read (d).File!,
			(f, o) => ((MusepackFile)f).Render (o)),
		["Mp4"] = new (
			() => TestBuilders.Mp4.CreateWithMetadata ("t"),
			d => Mp4File.Read (d).File!,
			(f, o) => ((Mp4File)f).Render (o).ToArray ()),
		["Asf"] = new (
			() => AsfTestBuilder.CreateMinimalWma ("t"),
			d => AsfFile.Read (d).File!,
			(f, o) => ((AsfFile)f).Render (o)),
		["WavId3"] = new (
			() => WithId3 (TestBuilders.Wav.CreateMinimal ()),
			d => WavFile.Read (d).File!,
			(f, o) => ((WavFile)f).Render ().ToArray ()),
		["WavInfo"] = new (
			() => WithInfo (TestBuilders.Wav.CreateMinimal ()),
			d => WavFile.Read (d).File!,
			(f, o) => ((WavFile)f).Render ().ToArray (),
			MultiValue: false,
			Name: "IXYZ"),
		["Aiff"] = new (
			() => TestBuilders.Aiff.CreateMinimal (),
			d => WithTag (AiffFile.Read (d).File!),
			(f, o) => ((AiffFile)f).Render ().ToArray ()),
		["Dsf"] = new (
			() => TestBuilders.Dsf.CreateWithMetadata ("t"),
			d => DsfFile.Read (d).File!,
			(f, o) => ((DsfFile)f).Render ().ToArray ()),
		["Dff"] = new (
			() => TestBuilders.Dff.CreateWithId3v2 ("t"),
			d => DffFile.Read (d).File!,
			(f, o) => ((DffFile)f).Render ().ToArray ()),
	};

	public static IEnumerable<object[]> ContainerNames => Containers.Keys.Select (k => new object[] { k });

	[TestMethod]
	[DynamicData (nameof (ContainerNames))]
	public void CustomField_SurvivesSaveAndReopen (string container)
	{
		var c = Containers[container];
		var original = c.Build ();
		var file = c.Read (original);
		var values = c.MultiValue ? new[] { "first", "second" } : ["only"];

		Assert.IsNotNull (file.Tag, "the builder should give the file a tag");
		Assert.IsTrue (file.Tag.SupportsCustomFields);
		Assert.IsTrue (file.Tag.SetCustomField (c.Name, values));

		var reopened = c.Read (c.Render (file, original));

		Assert.IsNotNull (reopened.Tag);
		CollectionAssert.AreEqual (values, reopened.Tag.GetCustomField (c.Name).ToArray ());
	}

	[TestMethod]
	[DynamicData (nameof (ContainerNames))]
	public void CustomField_RemovedField_StaysRemovedAfterReopen (string container)
	{
		var c = Containers[container];
		var original = c.Build ();
		var file = c.Read (original);
		file.Tag!.SetCustomField (c.Name, "x");
		var saved = c.Render (file, original);

		var again = c.Read (saved);
		Assert.IsTrue (again.Tag!.RemoveCustomField (c.Name));
		var reopened = c.Read (c.Render (again, saved));

		Assert.AreEqual (0, reopened.Tag?.GetCustomField (c.Name).Count ?? 0);
	}

	[TestMethod]
	[DataRow ("WavId3")]
	[DataRow ("Aiff")]
	[DataRow ("Dsf")]
	[DataRow ("Dff")]
	public void Id3Container_TagWithOnlyACustomField_IsStillWritten (string container)
	{
		var c = Containers[container];
		var original = c.Build ();
		var file = c.Read (original);
		file.Tag!.Clear ();
		file.Tag.SetCustomField (Name, "kept");

		var reopened = c.Read (c.Render (file, original));

		Assert.IsNotNull (reopened.Tag);
		AssertValues (reopened.Tag.GetCustomField (Name), "kept");
	}

	[TestMethod]
	public void Mp3_WithBothTags_CustomFieldGoesToId3v2Only ()
	{
		var original = CreateMinimalMp3 ();
		var file = Mp3File.Read (original).File!;
		file.Id3v1Tag = new TagLibSharp2.Id3.Id3v1Tag { Title = "t" };

		Assert.IsTrue (file.Tag!.SetCustomField (Name, "x"));
		var reopened = Mp3File.Read (file.Render (original).Span).File!;

		AssertValues (reopened.Id3v2Tag!.GetCustomField (Name), "x");
		Assert.AreEqual ("t", reopened.Id3v1Tag!.Title);
	}

	[TestMethod]
	public void Mp3_Id3v23Tag_RoundTripsSeveralValues ()
	{
		var original = CreateMinimalMp3 (version: 3);
		var file = Mp3File.Read (original).File!;
		Assert.AreEqual (3, file.Id3v2Tag!.Version);

		Assert.IsTrue (file.Tag!.SetCustomField (Name, "a", "b"));
		var reopened = Mp3File.Read (file.Render (original).Span).File!;

		Assert.AreEqual (3, reopened.Id3v2Tag!.Version);
		AssertValues (reopened.Tag!.GetCustomField (Name), "a", "b");
	}

	[TestMethod]
	public void Flac_WithoutVorbisComment_RefusedNameAddsNoBlock ()
	{
		var original = TestBuilders.Flac.CreateWithPicture ();
		var file = FlacFile.Read (original).File!;
		Assert.IsNull (file.VorbisComment);

		Assert.IsFalse (file.Tag!.SetCustomField ("A=B", "x"));
		Assert.IsNull (file.VorbisComment);
		Assert.IsTrue (file.Tag.SetCustomField ("GOOD", "x"));
		Assert.IsNotNull (file.VorbisComment);

		var reopened = FlacFile.Read (file.Render (original).Span).File!;
		AssertValues (reopened.Tag!.GetCustomField ("good"), "x");
	}

	[TestMethod]
	public void Flac_UnchangedSave_IsByteIdenticalAfterCustomFieldRoundTrip ()
	{
		var original = TestBuilders.Flac.CreateWithVorbisComment ("t");
		var file = FlacFile.Read (original).File!;
		file.Tag!.SetCustomField (Name, "a", "b");
		var saved = file.Render (original).ToArray ();

		var again = FlacFile.Read (saved).File!;
		var resaved = again.Render (saved).ToArray ();

		CollectionAssert.AreEqual (saved, resaved);
	}

	static void AssertValues (IReadOnlyList<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual (expected, actual.ToArray ());

	static byte[] CreateMinimalMp3 () => CreateMinimalMp3 (4);

	static byte[] CreateMinimalMp3 (byte version)
	{
		using var builder = new BinaryDataBuilder (256);
		builder.AddStringLatin1 ("ID3");
		builder.Add (version);
		builder.Add (0x00); // revision
		builder.Add (0x00); // flags
		builder.Add (new byte[] { 0x00, 0x00, 0x00, 0x00 }); // empty tag
		builder.Add (new byte[] { 0xFF, 0xFB, 0x90, 0x00 }); // MPEG-1 Layer III frame header
		builder.AddZeros (413);
		return builder.ToBinaryData ().ToArray ();
	}

	static byte[] WithId3 (byte[] wav)
	{
		var file = WavFile.Read (wav).File!;
		file.Id3v2Tag = new Id3v2Tag { Title = "t" };
		return file.Render ().ToArray ();
	}

	static byte[] WithInfo (byte[] wav)
	{
		var file = WavFile.Read (wav).File!;
		file.InfoTag = new RiffInfoTag { Title = "t" };
		return file.Render ().ToArray ();
	}

	static AiffFile WithTag (AiffFile file)
	{
		file.Tag ??= new Id3v2Tag { Title = "t" };
		return file;
	}
}
