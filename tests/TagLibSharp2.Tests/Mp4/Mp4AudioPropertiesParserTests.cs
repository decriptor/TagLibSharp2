// Copyright (c) 2025 Stephen Shaw and contributors
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Buffers.Binary;
using System.Text;
using TagLibSharp2.Mp4;

namespace TagLibSharp2.Tests.Mp4;

/// <summary>
/// Unit tests for Mp4AudioPropertiesParser (standalone parser tests).
/// </summary>
[TestClass]
[TestCategory("Unit")]
[TestCategory("Mp4")]
public class Mp4AudioPropertiesParserTests
{
	[TestMethod]
	public void Parse_ValidAacFile_ExtractsDuration()
	{
		var mp4Data = CreateMinimalAacMp4(duration: TimeSpan.FromSeconds(180), timescale: 1000);

		var properties = Mp4AudioPropertiesParser.Parse(mp4Data);

		Assert.IsTrue(properties.IsValid);
		Assert.IsTrue(Math.Abs((properties.Duration - TimeSpan.FromSeconds(180)).TotalSeconds) < 1);
	}

	[TestMethod]
	public void Parse_ValidAacFile_ExtractsSampleRate()
	{
		var mp4Data = CreateMinimalAacMp4(sampleRate: 44100);

		var properties = Mp4AudioPropertiesParser.Parse(mp4Data);

		Assert.AreEqual(44100, properties.SampleRate);
	}

	[TestMethod]
	public void Parse_ValidAacFile_ExtractsChannels()
	{
		var mp4Data = CreateMinimalAacMp4(channels: 2);

		var properties = Mp4AudioPropertiesParser.Parse(mp4Data);

		Assert.AreEqual(2, properties.Channels);
	}

	[TestMethod]
	public void Parse_ValidAlacFile_ExtractsSampleRate()
	{
		var mp4Data = CreateMinimalAlacMp4(sampleRate: 48000);

		var properties = Mp4AudioPropertiesParser.Parse(mp4Data);

		Assert.AreEqual(48000, properties.SampleRate);
	}

	[TestMethod]
	public void Parse_ValidAlacFile_ExtractsChannels()
	{
		var mp4Data = CreateMinimalAlacMp4(channels: 2);

		var properties = Mp4AudioPropertiesParser.Parse(mp4Data);

		Assert.AreEqual(2, properties.Channels);
	}

	[TestMethod]
	public void Parse_ValidAlacFile_ExtractsBitsPerSample()
	{
		var mp4Data = CreateMinimalAlacMp4(bitsPerSample: 24);

		var properties = Mp4AudioPropertiesParser.Parse(mp4Data);

		Assert.AreEqual(24, properties.BitsPerSample);
	}

	[TestMethod]
	public void Parse_EmptyData_ReturnsEmpty()
	{
		var properties = Mp4AudioPropertiesParser.Parse(ReadOnlySpan<byte>.Empty);

		Assert.IsFalse(properties.IsValid);
	}

	[TestMethod]
	public void Parse_NoMoovBox_ReturnsEmpty()
	{
		var data = CreateFtypBox();

		var properties = Mp4AudioPropertiesParser.Parse(data);

		Assert.IsFalse(properties.IsValid);
	}

	/// <summary>
	/// Creates a minimal AAC MP4 file for testing.
	/// </summary>
	private static byte[] CreateMinimalAacMp4(
		TimeSpan? duration = null,
		uint timescale = 1000,
		int sampleRate = 44100,
		int channels = 2)
	{
		var durationValue = duration ?? TimeSpan.FromSeconds(60);
		var durationUnits = (uint)(durationValue.TotalSeconds * timescale);

		var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);

		// ftyp box
		WriteBox(writer, "ftyp", w =>
		{
			w.Write(Encoding.ASCII.GetBytes("M4A "));
			w.Write(BinaryPrimitives.ReverseEndianness(0u)); // minor_version
			w.Write(Encoding.ASCII.GetBytes("M4A mp42isom"));
		});

		// moov box
		WriteBox(writer, "moov", w =>
		{
			// mvhd box
			WriteFullBox(w, "mvhd", 0, 0, mw =>
			{
				mw.Write(BinaryPrimitives.ReverseEndianness(0u)); // creation_time
				mw.Write(BinaryPrimitives.ReverseEndianness(0u)); // modification_time
				mw.Write(BinaryPrimitives.ReverseEndianness(timescale));
				mw.Write(BinaryPrimitives.ReverseEndianness(durationUnits));
				// Skip remaining mvhd fields (rate, volume, matrix, etc.)
				mw.Write(new byte[68]);
			});

			// trak box (audio track)
			WriteBox(w, "trak", tw =>
			{
				// mdia box
				WriteBox(tw, "mdia", mw =>
				{
					// mdhd box
					WriteFullBox(mw, "mdhd", 0, 0, mdhdw =>
					{
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(0u)); // creation_time
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(0u)); // modification_time
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(timescale));
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(durationUnits));
						mdhdw.Write(BinaryPrimitives.ReverseEndianness((ushort)0x55C4)); // language (und)
						mdhdw.Write(BinaryPrimitives.ReverseEndianness((ushort)0)); // pre_defined
					});

					// hdlr box (handler = 'soun' for audio)
					WriteFullBox(mw, "hdlr", 0, 0, hw =>
					{
						hw.Write(BinaryPrimitives.ReverseEndianness(0u)); // pre_defined
						hw.Write(Encoding.ASCII.GetBytes("soun"));
						hw.Write(new byte[12]); // reserved
						hw.Write(Encoding.ASCII.GetBytes("SoundHandler\0"));
					});

					// minf box
					WriteBox(mw, "minf", minfW =>
					{
						// smhd box (sound media header)
						WriteFullBox(minfW, "smhd", 0, 0, smhdW =>
						{
							smhdW.Write(BinaryPrimitives.ReverseEndianness((ushort)0)); // balance
							smhdW.Write(BinaryPrimitives.ReverseEndianness((ushort)0)); // reserved
						});

						// dinf box
						WriteBox(minfW, "dinf", dinfW =>
						{
							WriteFullBox(dinfW, "dref", 0, 0, drefW =>
							{
								drefW.Write(BinaryPrimitives.ReverseEndianness(0u)); // entry_count
							});
						});

						// stbl box
						WriteBox(minfW, "stbl", stblW =>
						{
							// stsd box (sample description)
							WriteFullBox(stblW, "stsd", 0, 0, stsdW =>
							{
								stsdW.Write(BinaryPrimitives.ReverseEndianness(1u)); // entry_count

								// mp4a sample entry
								WriteBox(stsdW, "mp4a", mp4aW =>
								{
									mp4aW.Write(new byte[6]); // reserved
									mp4aW.Write(BinaryPrimitives.ReverseEndianness((ushort)1)); // data_reference_index
									mp4aW.Write(new byte[8]); // reserved
									mp4aW.Write(BinaryPrimitives.ReverseEndianness((ushort)channels));
									mp4aW.Write(BinaryPrimitives.ReverseEndianness((ushort)16)); // sample_size
									mp4aW.Write(BinaryPrimitives.ReverseEndianness(0u)); // reserved
									mp4aW.Write(BinaryPrimitives.ReverseEndianness((uint)(sampleRate << 16))); // sample_rate (16.16 fixed)

									// esds box
									WriteFullBox(mp4aW, "esds", 0, 0, esdsW =>
									{
										// ES_Descriptor tag
										esdsW.Write((byte)0x03); // ES_DescrTag
										esdsW.Write((byte)0x19); // size
										esdsW.Write(BinaryPrimitives.ReverseEndianness((ushort)0)); // ES_ID
										esdsW.Write((byte)0); // flags

										// DecoderConfigDescriptor tag
										esdsW.Write((byte)0x04); // DecoderConfigDescrTag
										esdsW.Write((byte)0x11); // size
										esdsW.Write((byte)0x40); // objectTypeIndication (AAC-LC)
										esdsW.Write((byte)0x15); // streamType
										esdsW.Write(new byte[3]); // bufferSizeDB (24-bit)
										esdsW.Write(BinaryPrimitives.ReverseEndianness(256000u)); // maxBitrate
										esdsW.Write(BinaryPrimitives.ReverseEndianness(128000u)); // avgBitrate

										// DecoderSpecificInfo tag (Audio Specific Config)
										esdsW.Write((byte)0x05); // DecSpecificInfoTag
										esdsW.Write((byte)0x02); // size

										// Audio Specific Config (2 bytes)
										// Bits: audioObjectType(5) + samplingFrequencyIndex(4) + channelConfiguration(4) + ...
										var samplingFrequencyIndex = GetSamplingFrequencyIndex(sampleRate);
										var audioObjectType = 2; // AAC-LC
										var channelConfiguration = channels;

										var asc = (ushort)((audioObjectType << 11) | (samplingFrequencyIndex << 7) | (channelConfiguration << 3));
										esdsW.Write(BinaryPrimitives.ReverseEndianness(asc));

										// SLConfigDescriptor tag
										esdsW.Write((byte)0x06); // SLConfigDescrTag
										esdsW.Write((byte)0x01); // size
										esdsW.Write((byte)0x02); // predefined
									});
								});
							});
						});
					});
				});
			});
		});

		return stream.ToArray();
	}

	/// <summary>
	/// Creates a minimal ALAC MP4 file for testing.
	/// </summary>
	private static byte[] CreateMinimalAlacMp4(
		int sampleRate = 44100,
		int channels = 2,
		int bitsPerSample = 16)
	{
		var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);

		// ftyp box
		WriteBox(writer, "ftyp", w =>
		{
			w.Write(Encoding.ASCII.GetBytes("M4A "));
			w.Write(BinaryPrimitives.ReverseEndianness(0u));
			w.Write(Encoding.ASCII.GetBytes("M4A mp42isom"));
		});

		// moov box
		WriteBox(writer, "moov", w =>
		{
			// mvhd box (simplified)
			WriteFullBox(w, "mvhd", 0, 0, mw =>
			{
				mw.Write(BinaryPrimitives.ReverseEndianness(0u)); // creation_time
				mw.Write(BinaryPrimitives.ReverseEndianness(0u)); // modification_time
				mw.Write(BinaryPrimitives.ReverseEndianness(1000u)); // timescale
				mw.Write(BinaryPrimitives.ReverseEndianness(60000u)); // duration
				mw.Write(new byte[68]);
			});

			// trak box
			WriteBox(w, "trak", tw =>
			{
				WriteBox(tw, "mdia", mw =>
				{
					WriteFullBox(mw, "mdhd", 0, 0, mdhdw =>
					{
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(0u));
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(0u));
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(1000u)); // timescale
						mdhdw.Write(BinaryPrimitives.ReverseEndianness(60000u)); // duration
						mdhdw.Write(BinaryPrimitives.ReverseEndianness((ushort)0x55C4));
						mdhdw.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
					});

					WriteFullBox(mw, "hdlr", 0, 0, hw =>
					{
						hw.Write(BinaryPrimitives.ReverseEndianness(0u));
						hw.Write(Encoding.ASCII.GetBytes("soun"));
						hw.Write(new byte[12]);
						hw.Write(Encoding.ASCII.GetBytes("SoundHandler\0"));
					});

					WriteBox(mw, "minf", minfW =>
					{
						WriteFullBox(minfW, "smhd", 0, 0, smhdW =>
						{
							smhdW.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
							smhdW.Write(BinaryPrimitives.ReverseEndianness((ushort)0));
						});

						WriteBox(minfW, "dinf", dinfW =>
						{
							WriteFullBox(dinfW, "dref", 0, 0, drefW =>
							{
								drefW.Write(BinaryPrimitives.ReverseEndianness(0u));
							});
						});

						WriteBox(minfW, "stbl", stblW =>
						{
							WriteFullBox(stblW, "stsd", 0, 0, stsdW =>
							{
								stsdW.Write(BinaryPrimitives.ReverseEndianness(1u));

								// alac sample entry
								WriteBox(stsdW, "alac", alacW =>
								{
									alacW.Write(new byte[6]);
									alacW.Write(BinaryPrimitives.ReverseEndianness((ushort)1));
									alacW.Write(new byte[8]);
									alacW.Write(BinaryPrimitives.ReverseEndianness((ushort)channels));
									alacW.Write(BinaryPrimitives.ReverseEndianness((ushort)bitsPerSample));
									alacW.Write(BinaryPrimitives.ReverseEndianness(0u));
									alacW.Write(BinaryPrimitives.ReverseEndianness((uint)(sampleRate << 16)));

									// alac magic cookie box
									WriteBox(alacW, "alac", cookieW =>
									{
										// 36-byte ALAC magic cookie
										cookieW.Write(BinaryPrimitives.ReverseEndianness(4096u)); // frame_length
										cookieW.Write(BinaryPrimitives.ReverseEndianness(0u)); // compatible_version
										cookieW.Write((byte)bitsPerSample); // sample_size
										cookieW.Write((byte)40); // rice_history_mult
										cookieW.Write((byte)10); // rice_initial_history
										cookieW.Write((byte)14); // rice_parameter_limit
										cookieW.Write((byte)channels); // channels
										cookieW.Write(BinaryPrimitives.ReverseEndianness((ushort)255)); // max_run
										cookieW.Write(BinaryPrimitives.ReverseEndianness(0u)); // max_coded_frame_size
										cookieW.Write(BinaryPrimitives.ReverseEndianness(0u)); // avg_bitrate
										cookieW.Write(BinaryPrimitives.ReverseEndianness((uint)sampleRate)); // sample_rate
									});
								});
							});
						});
					});
				});
			});
		});

		return stream.ToArray();
	}

	private static byte[] CreateFtypBox()
	{
		var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		WriteBox(writer, "ftyp", w =>
		{
			w.Write(Encoding.ASCII.GetBytes("M4A "));
			w.Write(BinaryPrimitives.ReverseEndianness(0u));
			w.Write(Encoding.ASCII.GetBytes("M4A mp42isom"));
		});
		return stream.ToArray();
	}

	private static void WriteBox(BinaryWriter writer, string type, Action<BinaryWriter> writeContent)
	{
		var contentStream = new MemoryStream();
		var contentWriter = new BinaryWriter(contentStream);
		writeContent(contentWriter);
		var content = contentStream.ToArray();

		writer.Write(BinaryPrimitives.ReverseEndianness((uint)(8 + content.Length)));
		writer.Write(Encoding.ASCII.GetBytes(type));
		writer.Write(content);
	}

	private static void WriteFullBox(BinaryWriter writer, string type, byte version, uint flags, Action<BinaryWriter> writeContent)
	{
		WriteBox(writer, type, w =>
		{
			w.Write(version);
			w.Write((byte)((flags >> 16) & 0xFF));
			w.Write((byte)((flags >> 8) & 0xFF));
			w.Write((byte)(flags & 0xFF));
			writeContent(w);
		});
	}

	private static int GetSamplingFrequencyIndex(int sampleRate)
	{
		return sampleRate switch
		{
			96000 => 0,
			88200 => 1,
			64000 => 2,
			48000 => 3,
			44100 => 4,
			32000 => 5,
			24000 => 6,
			22050 => 7,
			16000 => 8,
			12000 => 9,
			11025 => 10,
			8000 => 11,
			7350 => 12,
			_ => 4 // default to 44100
		};
	}
}
