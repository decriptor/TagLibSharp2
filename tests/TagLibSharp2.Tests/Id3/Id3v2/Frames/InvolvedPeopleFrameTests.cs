// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using TagLibSharp2.Id3.Id3v2;
using TagLibSharp2.Id3.Id3v2.Frames;

namespace TagLibSharp2.Tests.Id3.Id3v2.Frames;

/// <summary>
/// Tests for InvolvedPeopleFrame (TIPL, TMCL, IPLS).
/// </summary>
[TestClass]
[TestCategory ("Unit")]
[TestCategory ("Id3")]
[TestCategory ("Id3v2")]
[TestCategory ("Frame")]
public class InvolvedPeopleFrameTests
{
	// Basic Construction Tests

	[TestMethod]
	public void Constructor_WithEmptyPairs_CreatesEmptyFrame ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);

		Assert.AreEqual ("TMCL", frame.Id);
		Assert.AreEqual (0, frame.Count);
	}

	[TestMethod]
	public void Constructor_WithTipl_CreatesCorrectFrameId ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.InvolvedPeople);

		Assert.AreEqual ("TIPL", frame.Id);
	}

	[TestMethod]
	public void Constructor_WithTmcl_CreatesCorrectFrameId ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);

		Assert.AreEqual ("TMCL", frame.Id);
	}

	// Add/Get Tests

	[TestMethod]
	public void Add_SinglePair_Works ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);

		frame.Add ("guitar", "John Smith");

		Assert.AreEqual (1, frame.Count);
		Assert.AreEqual ("John Smith", frame.GetPerson ("guitar"));
	}

	[TestMethod]
	public void Add_MultiplePairs_Works ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);

		frame.Add ("guitar", "John Smith");
		frame.Add ("drums", "Jane Doe");
		frame.Add ("bass", "Bob Wilson");

		Assert.AreEqual (3, frame.Count);
		Assert.AreEqual ("John Smith", frame.GetPerson ("guitar"));
		Assert.AreEqual ("Jane Doe", frame.GetPerson ("drums"));
		Assert.AreEqual ("Bob Wilson", frame.GetPerson ("bass"));
	}

	[TestMethod]
	public void GetPerson_NonExistentRole_ReturnsNull ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");

		var result = frame.GetPerson ("piano");

		Assert.IsNull (result);
	}

	[TestMethod]
	public void GetRoles_ReturnsAllRoles ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");
		frame.Add ("drums", "Jane Doe");

		var roles = frame.GetRoles ();

		Assert.HasCount (2, roles);
		Assert.IsTrue (roles.Contains ("guitar"));
		Assert.IsTrue (roles.Contains ("drums"));
	}

	[TestMethod]
	public void GetPeople_ReturnsAllPeople ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");
		frame.Add ("drums", "Jane Doe");

		var people = frame.GetPeople ();

		Assert.HasCount (2, people);
		Assert.IsTrue (people.Contains ("John Smith"));
		Assert.IsTrue (people.Contains ("Jane Doe"));
	}

	// Clear/Remove Tests

	[TestMethod]
	public void Clear_RemovesAllPairs ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");
		frame.Add ("drums", "Jane Doe");

		frame.Clear ();

		Assert.AreEqual (0, frame.Count);
	}

	[TestMethod]
	public void Remove_ExistingRole_RemovesPair ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");
		frame.Add ("drums", "Jane Doe");

		var removed = frame.Remove ("guitar");

		Assert.IsTrue (removed);
		Assert.AreEqual (1, frame.Count);
		Assert.IsNull (frame.GetPerson ("guitar"));
		Assert.AreEqual ("Jane Doe", frame.GetPerson ("drums"));
	}

	[TestMethod]
	public void Remove_NonExistentRole_ReturnsFalse ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");

		var removed = frame.Remove ("piano");

		Assert.IsFalse (removed);
		Assert.AreEqual (1, frame.Count);
	}

	// Read/Parse Tests

	[TestMethod]
	public void Read_ValidTmclFrame_ParsesPairs ()
	{
		// UTF-8 encoding, "guitar\0John Smith\0drums\0Jane Doe"
		var data = new byte[] {
			0x03, // UTF-8 encoding
			(byte)'g', (byte)'u', (byte)'i', (byte)'t', (byte)'a', (byte)'r', 0x00,
			(byte)'J', (byte)'o', (byte)'h', (byte)'n', (byte)' ', (byte)'S', (byte)'m', (byte)'i', (byte)'t', (byte)'h', 0x00,
			(byte)'d', (byte)'r', (byte)'u', (byte)'m', (byte)'s', 0x00,
			(byte)'J', (byte)'a', (byte)'n', (byte)'e', (byte)' ', (byte)'D', (byte)'o', (byte)'e'
		};

		var result = InvolvedPeopleFrame.Read ("TMCL", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (2, result.Frame!.Count);
		Assert.AreEqual ("John Smith", result.Frame.GetPerson ("guitar"));
		Assert.AreEqual ("Jane Doe", result.Frame.GetPerson ("drums"));
	}

	[TestMethod]
	public void Read_ValidTiplFrame_ParsesPairs ()
	{
		// UTF-8 encoding, "producer\0Phil Spector"
		var data = new byte[] {
			0x03, // UTF-8 encoding
			(byte)'p', (byte)'r', (byte)'o', (byte)'d', (byte)'u', (byte)'c', (byte)'e', (byte)'r', 0x00,
			(byte)'P', (byte)'h', (byte)'i', (byte)'l', (byte)' ', (byte)'S', (byte)'p', (byte)'e', (byte)'c', (byte)'t', (byte)'o', (byte)'r'
		};

		var result = InvolvedPeopleFrame.Read ("TIPL", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Frame!.Count);
		Assert.AreEqual ("Phil Spector", result.Frame.GetPerson ("producer"));
	}

	[TestMethod]
	public void Read_EmptyData_ReturnsFailure ()
	{
		var result = InvolvedPeopleFrame.Read ("TMCL", ReadOnlySpan<byte>.Empty, Id3v2Version.V24);

		Assert.IsFalse (result.IsSuccess);
	}

	[TestMethod]
	public void Read_OnlyEncodingByte_ReturnsEmptyFrame ()
	{
		var data = new byte[] { 0x03 }; // Only UTF-8 encoding byte

		var result = InvolvedPeopleFrame.Read ("TMCL", data, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (0, result.Frame!.Count);
	}

	// Render Tests

	[TestMethod]
	public void RenderContent_EmptyFrame_ReturnsEncodingByteOnly ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);

		var content = frame.RenderContent ();

		Assert.AreEqual (1, content.Length);
		Assert.AreEqual (0x03, content.Span[0]); // UTF-8 encoding
	}

	[TestMethod]
	public void RenderContent_WithPairs_CreatesNullSeparatedData ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John");

		var content = frame.RenderContent ();

		Assert.IsGreaterThan (1, content.Length);
		Assert.AreEqual (0x03, content.Span[0]); // UTF-8 encoding
	}

	// Round-trip Tests

	[TestMethod]
	public void RoundTrip_SinglePair_PreservesData ()
	{
		var original = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		original.Add ("guitar", "John Smith");

		var content = original.RenderContent ();
		var result = InvolvedPeopleFrame.Read ("TMCL", content.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Frame!.Count);
		Assert.AreEqual ("John Smith", result.Frame.GetPerson ("guitar"));
	}

	[TestMethod]
	public void RoundTrip_MultiplePairs_PreservesData ()
	{
		var original = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		original.Add ("lead vocals", "Freddie Mercury");
		original.Add ("guitar", "Brian May");
		original.Add ("bass", "John Deacon");
		original.Add ("drums", "Roger Taylor");

		var content = original.RenderContent ();
		var result = InvolvedPeopleFrame.Read ("TMCL", content.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (4, result.Frame!.Count);
		Assert.AreEqual ("Freddie Mercury", result.Frame.GetPerson ("lead vocals"));
		Assert.AreEqual ("Brian May", result.Frame.GetPerson ("guitar"));
		Assert.AreEqual ("John Deacon", result.Frame.GetPerson ("bass"));
		Assert.AreEqual ("Roger Taylor", result.Frame.GetPerson ("drums"));
	}

	[TestMethod]
	public void RoundTrip_Unicode_PreservesCharacters ()
	{
		var original = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		original.Add ("ボーカル", "田中太郎"); // Japanese

		var content = original.RenderContent ();
		var result = InvolvedPeopleFrame.Read ("TMCL", content.Span, Id3v2Version.V24);

		Assert.IsTrue (result.IsSuccess);
		Assert.AreEqual (1, result.Frame!.Count);
		Assert.AreEqual ("田中太郎", result.Frame.GetPerson ("ボーカル"));
	}

	// TIPL-specific tests (production credits)

	[TestMethod]
	public void InvolvedPeopleFrame_Tipl_StoresProductionCredits ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.InvolvedPeople);
		frame.Add ("producer", "Phil Spector");
		frame.Add ("engineer", "Eddie Kramer");
		frame.Add ("mixing", "Bob Clearmountain");

		Assert.AreEqual ("TIPL", frame.Id);
		Assert.AreEqual (3, frame.Count);
		Assert.AreEqual ("Phil Spector", frame.GetPerson ("producer"));
		Assert.AreEqual ("Eddie Kramer", frame.GetPerson ("engineer"));
		Assert.AreEqual ("Bob Clearmountain", frame.GetPerson ("mixing"));
	}

	// Edge cases

	[TestMethod]
	public void Add_DuplicateRole_ReplacesExisting ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("guitar", "John Smith");
		frame.Add ("guitar", "Jane Doe"); // Replace

		Assert.AreEqual (1, frame.Count);
		Assert.AreEqual ("Jane Doe", frame.GetPerson ("guitar"));
	}

	[TestMethod]
	public void GetPerson_CaseInsensitiveMatch ()
	{
		var frame = new InvolvedPeopleFrame (InvolvedPeopleFrameType.MusicianCredits);
		frame.Add ("Guitar", "John Smith");

		// Should match case-insensitively
		Assert.AreEqual ("John Smith", frame.GetPerson ("guitar"));
		Assert.AreEqual ("John Smith", frame.GetPerson ("GUITAR"));
	}
}
