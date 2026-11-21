// Copyright (c) 2025-2026 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Text;
using TagLibSharp2.Core;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Audible .aax files are MPEG-4. Their audiobook atoms map to named properties.
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Mp4")]
[TestCategory ("Audible")]
public class Mp4AudibleAtomTests
{
	static readonly (string Atom, Action<Mp4Tag, string?> Set, Func<Mp4Tag, string?> Get, Func<Mp4File, string?> FileGet, Action<Mp4File, string?> FileSet)[] Atoms = [
		("©nrt", (t, v) => t.Narrator = v, t => t.Narrator, f => f.Narrator, (f, v) => f.Narrator = v),
		("@sti", (t, v) => t.ShortTitle = v, t => t.ShortTitle, f => f.ShortTitle, (f, v) => f.ShortTitle = v),
		("@pti", (t, v) => t.ParentTitle = v, t => t.ParentTitle, f => f.ParentTitle, (f, v) => f.ParentTitle = v),
		("@PST", (t, v) => t.ParentShortTitle = v, t => t.ParentShortTitle, f => f.ParentShortTitle, (f, v) => f.ParentShortTitle = v),
		("@ppi", (t, v) => t.ParentProductId = v, t => t.ParentProductId, f => f.ParentProductId, (f, v) => f.ParentProductId = v),
		("prID", (t, v) => t.ProductId = v, t => t.ProductId, f => f.ProductId, (f, v) => f.ProductId = v),
		("rldt", (t, v) => t.ReleaseDate = v, t => t.ReleaseDate, f => f.ReleaseDate, (f, v) => f.ReleaseDate = v),
		("VERS", (t, v) => t.ProductVersion = v, t => t.ProductVersion, f => f.ProductVersion, (f, v) => f.ProductVersion = v),
	];

	[TestMethod]
	public void AudibleAtoms_RoundTripUnderTheirExactCodes ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);

		foreach (var (atom, set, get, _, _) in Atoms) {
			var file = Mp4File.Read (data).File!;
			file.Tag ??= new Mp4Tag ();
			set (file.Tag, "Value for " + atom);

			var rendered = file.Render (data).ToArray ();

			// '@' is 0x40 and '©' is 0xA9: the codes are Latin-1 bytes, not UTF-8.
			var code = Encoding.Latin1.GetBytes (atom);
			Assert.IsTrue (IndexOf (rendered, code) >= 0, $"{atom} not written");
			var reread = Mp4File.Read (rendered).File!;
			Assert.AreEqual ("Value for " + atom, get (reread.Tag!), atom);

			set (reread.Tag!, null);
			Assert.IsNull (get (Mp4File.Read (reread.Render (rendered).Span).File!.Tag ?? new Mp4Tag ()), atom);
		}
	}

	[TestMethod]
	public void AudibleAtoms_FileForwardersMatchTheTag ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);

		foreach (var (atom, _, get, fileGet, fileSet) in Atoms) {
			var file = Mp4File.Read (data).File!;
			Assert.IsNull (fileGet (file), atom);

			fileSet (file, "Forwarded " + atom);

			Assert.AreEqual ("Forwarded " + atom, fileGet (file), atom);
			Assert.AreEqual ("Forwarded " + atom, get (file.Tag!), atom);
		}
	}

	[TestMethod]
	[DataRow ("CDEK")]
	[DataRow ("CDET")]
	[DataRow ("AACR")]
	[DataRow ("GUID")]
	public void UnnamedAudibleItem_SurvivesASave (string code)
	{
		// Audible writes these items too; nothing maps them, so a save must carry them through.
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (data).File!;
		file.ProductVersion = "opaque value";
		var withItem = file.Render (data).ToArray ();
		Encoding.ASCII.GetBytes (code).CopyTo (withItem, IndexOf (withItem, "VERS"u8.ToArray ()));

		var reread = Mp4File.Read (withItem).File!;
		Assert.IsNull (reread.ProductVersion);
		reread.Narrator = "Changed";
		var saved = reread.Render (withItem).ToArray ();

		Assert.IsTrue (IndexOf (saved, Encoding.ASCII.GetBytes (code)) >= 0, $"{code} dropped on save");
		Assert.IsTrue (IndexOf (saved, "opaque value"u8.ToArray ()) >= 0);
		Assert.AreEqual ("Changed", Mp4File.Read (saved).File!.Narrator);
	}

	[TestMethod]
	public void AudibleAtom_SetOnAFileThatHasIt_LeavesOneItem ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var file = Mp4File.Read (data).File!;
		file.Narrator = "First";
		var first = file.Render (data).ToArray ();

		var again = Mp4File.Read (first).File!;
		again.Narrator = "Second";
		var saved = again.Render (first).ToArray ();

		var code = Encoding.Latin1.GetBytes ("\u00A9nrt");
		var at = IndexOf (saved, code);
		Assert.IsTrue (at >= 0);
		Assert.AreEqual (-1, IndexOf (saved.AsSpan (at + 4).ToArray (), code), "a second narrator item");
		Assert.AreEqual (-1, IndexOf (saved, "First"u8.ToArray ()));
		Assert.AreEqual ("Second", Mp4File.Read (saved).File!.Narrator);
	}

	[TestMethod]
	public void AudibleAtoms_InAQuickTimeKeyedMeta_RoundTrip ()
	{
		// An mdta meta names items through keys. An Audible atom has no key name, so it is written
		// under a "udta"-namespace key holding its atom code, and read back through that key.
		var data = TestBuilders.Mp4.CreateWithKeyedMeta ("mdta", TestBuilders.Mp4.KeysPayload ("title"), [TestBuilders.Mp4.CreateMdtaItem (1, "T")], inMovie: false, udtaExtra: null);

		foreach (var (atom, set, get, _, _) in Atoms) {
			var file = Mp4File.Read (data).File!;
			set (file.Tag!, "Keyed " + atom);

			var reread = Mp4File.Read (file.Render (data).Span).File!;

			Assert.AreEqual ("Keyed " + atom, get (reread.Tag!), atom);
			Assert.AreEqual ("T", reread.Tag!.Title, atom);
		}
	}

	[TestMethod]
	public void AaxBrand_OpensAsMp4WithTheBrandAsFileType ()
	{
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		Encoding.ASCII.GetBytes ("aax ").CopyTo (data, 8);

		var result = MediaFile.ReadFromData (data, "book.aax");

		Assert.IsTrue (result.IsSuccess, result.Error);
		Assert.AreEqual (MediaFormat.Mp4, result.Format);
		Assert.AreEqual ("aax", ((Mp4File)result.File!).FileType);
	}

	[TestMethod]
	public void AavdSampleEntry_IsAac ()
	{
		// .aax keeps AAC in an "aavd" sample entry with the mp4a layout and esds.
		var data = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		var at = IndexOf (data, "mp4a"u8.ToArray ());
		Encoding.ASCII.GetBytes ("aavd").CopyTo (data, at);

		var file = Mp4File.Read (data).File!;

		Assert.AreEqual (Mp4AudioCodec.Aac, file.AudioCodec);
		Assert.AreEqual ("AAC", file.Properties.Codec);
		var mp4a = TestBuilders.Mp4.CreateMinimalM4a (Mp4CodecType.Aac);
		Assert.AreEqual (Mp4File.Read (mp4a).File!.Properties, file.Properties);
		Assert.AreEqual (Mp4AudioPropertiesParser.Parse (mp4a), Mp4AudioPropertiesParser.Parse (data));
	}

	static int IndexOf (byte[] haystack, byte[] needle)
	{
		for (var i = 0; i + needle.Length <= haystack.Length; i++) {
			if (haystack.AsSpan (i, needle.Length).SequenceEqual (needle))
				return i;
		}
		return -1;
	}
}
