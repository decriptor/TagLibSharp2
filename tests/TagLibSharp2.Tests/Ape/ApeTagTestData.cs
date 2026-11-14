// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Ape;
using TagLibSharp2.Musepack;
using TagLibSharp2.WavPack;

namespace TagLibSharp2.Tests.Ape;

/// <summary>
/// Shared APE suffix builders used by Monkey's Audio, WavPack, and Musepack tests.
/// </summary>
internal static class ApeTagTestData
{
	public static byte[] CreateAudioWithApeThenId3v1 (byte[] audio, string title)
	{
		var ape = new ApeTag { Title = title }.RenderWithOptions (includeHeader: true).ToArray ();
		var id3 = new byte[128];
		"TAG"u8.CopyTo (id3);
		"KeepMe"u8.CopyTo (id3.AsSpan (3));

		var data = new byte[audio.Length + ape.Length + id3.Length];
		audio.CopyTo (data, 0);
		ape.CopyTo (data, audio.Length);
		id3.CopyTo (data, audio.Length + ape.Length);
		return data;
	}

	/// <summary>
	/// Save-with-ID3v1 checks shared by Monkey's Audio, WavPack, and Musepack.
	/// Rewrites the one APE tag in front of ID3v1, then removes it and leaves the audio plus ID3v1.
	/// </summary>
	public static void AssertId3v1SaveRoundTrip (byte[] audio, Func<byte[], ApeId3v1SaveSession> read)
	{
		var data = CreateAudioWithApeThenId3v1 (audio, "Original");
		var opened = read (data);
		Assert.IsTrue (opened.IsSuccess, opened.Error);

		opened.Tag!.Title = "New Title";
		var rendered = opened.Render (data);

		Assert.AreEqual (1, CountSequence (rendered, "APETAGEX"u8));
		Assert.IsTrue (rendered.AsSpan (rendered.Length - 128 - ApeTagFooter.Size, 8).SequenceEqual ("APETAGEX"u8));
		Assert.IsTrue (rendered.AsSpan (rendered.Length - 128, 3).SequenceEqual ("TAG"u8));
		Assert.IsTrue (rendered.AsSpan (rendered.Length - 125, 6).SequenceEqual ("KeepMe"u8));
		CollectionAssert.AreEqual (audio, rendered[..audio.Length]);

		var reparsed = read (rendered);
		Assert.IsTrue (reparsed.IsSuccess, reparsed.Error);
		Assert.AreEqual ("New Title", reparsed.Tag!.Title);

		opened.RemoveTag ();
		var removed = opened.Render (data);
		Assert.AreEqual (0, CountSequence (removed, "APETAGEX"u8));
		Assert.AreEqual (audio.Length + 128, removed.Length);
		CollectionAssert.AreEqual (audio, removed[..audio.Length]);
		Assert.IsTrue (removed.AsSpan (audio.Length, 3).SequenceEqual ("TAG"u8));

		var stripped = read (removed);
		Assert.IsTrue (stripped.IsSuccess, stripped.Error);
		Assert.IsNull (stripped.Tag);
	}

	public static ApeId3v1SaveSession OpenMonkeysAudio (byte[] data)
	{
		var result = MonkeysAudioFile.Read (data);
		var file = result.File;
		return file is null
			? ApeId3v1SaveSession.Missing (result.Error)
			: new ApeId3v1SaveSession (
				result.IsSuccess,
				result.Error,
				file.ApeTag,
				file.RemoveApeTag,
				original => file.Render (original));
	}

	public static ApeId3v1SaveSession OpenWavPack (byte[] data)
	{
		var result = WavPackFile.Read (data);
		var file = result.File;
		return file is null
			? ApeId3v1SaveSession.Missing (result.Error)
			: new ApeId3v1SaveSession (
				result.IsSuccess,
				result.Error,
				file.ApeTag,
				file.RemoveApeTag,
				original => file.Render (original));
	}

	public static ApeId3v1SaveSession OpenMusepack (byte[] data)
	{
		var result = MusepackFile.Read (data);
		var file = result.File;
		return file is null
			? ApeId3v1SaveSession.Missing (result.Error)
			: new ApeId3v1SaveSession (
				result.IsSuccess,
				result.Error,
				file.ApeTag,
				file.RemoveApeTag,
				original => file.Render (original));
	}

	public static int CountSequence (ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
	{
		var count = 0;
		for (var i = 0; i <= data.Length - needle.Length; i++) {
			if (data.Slice (i, needle.Length).SequenceEqual (needle))
				count++;
		}

		return count;
	}
}

/// <summary>
/// One opened APE file under test: its tag, and the render/remove operations on that same instance.
/// </summary>
internal sealed class ApeId3v1SaveSession
{
	public ApeId3v1SaveSession (
		bool isSuccess,
		string? error,
		ApeTag? tag,
		Action removeTag,
		Func<byte[], byte[]> render)
	{
		IsSuccess = isSuccess;
		Error = error;
		Tag = tag;
		RemoveTag = removeTag;
		Render = render;
	}

	public bool IsSuccess { get; }

	public string? Error { get; }

	public ApeTag? Tag { get; }

	public Action RemoveTag { get; }

	public Func<byte[], byte[]> Render { get; }

	public static ApeId3v1SaveSession Missing (string? error) =>
		new (false, error, null, static () => { }, static _ => []);
}
