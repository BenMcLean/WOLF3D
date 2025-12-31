using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace BenMcLean.Wolf3D.Assets.Sound;

/// <summary>
/// MIDI sequence parser for Super 3-D Noah's Ark music playback.
/// Parses standard MIDI Format 0/1 files into structured event lists.
/// Based on ID_SD.C:2029-2433 from Super 3-D Noah's Ark source code.
/// </summary>
public class Midi
{
	// MIDI header fields (MIDI Standard, MThd chunk)
	public ushort Format { get; set; }              // MIDI format type (0 or 1)
	public ushort NumTracks { get; set; }           // Number of tracks
	public ushort TicksPerQuarterNote { get; set; } // Timing division
	public List<MidiEvent> Events { get; set; } = new();
	/// <summary>
	/// Parses a MIDI file from raw bytes (after skipping the 2-byte MusicGroup.length header).
	/// </summary>
	public static Midi Parse(byte[] data)
	{
		using MemoryStream stream = new(data);
		using BinaryReader reader = new(stream);
		return Parse(reader);
	}
	/// <summary>
	/// Parses a MIDI file from a BinaryReader.
	/// Expects standard MIDI format starting with "MThd" header.
	/// </summary>
	public static Midi Parse(BinaryReader reader)
	{
		Midi midi = new();

		// Read MIDI header ("MThd")
		byte[] header = reader.ReadBytes(4);
		if (header.Length != 4 || header[0] != 'M' || header[1] != 'T' || header[2] != 'h' || header[3] != 'd')
			throw new InvalidDataException("MIDI header 'MThd' expected");
		uint headerLength = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
		if (headerLength < 6)
			throw new InvalidDataException("MIDI header too short");
		midi.Format = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
		midi.NumTracks = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
		midi.TicksPerQuarterNote = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
		// Skip any extra header bytes
		if (headerLength > 6)
			reader.ReadBytes((int)(headerLength - 6));
		// Validate format
		if (midi.Format != 0 && midi.Format != 1)
			throw new InvalidDataException($"Unsupported MIDI format: {midi.Format} (only Format 0 and 1 supported)");
		// Read track(s)
		for (int track = 0; track < midi.NumTracks; track++)
		{
			// Read track header ("MTrk")
			byte[] trackHeader = reader.ReadBytes(4);
			if (trackHeader.Length != 4 || trackHeader[0] != 'M' || trackHeader[1] != 'T' || trackHeader[2] != 'r' || trackHeader[3] != 'k')
				throw new InvalidDataException("MIDI track header 'MTrk' expected");
			uint trackLength = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
			if (trackLength == 0)
				throw new InvalidDataException("MIDI track is 0 length");
			long trackEnd = reader.BaseStream.Position + trackLength;
			// Parse events in this track
			byte runningStatus = 0;
			while (reader.BaseStream.Position < trackEnd)
			{
				uint deltaTime = ReadVariableLength(reader);
				byte statusByte = reader.ReadByte();
				// Handle running status (if high bit is 0, reuse previous status)
				if ((statusByte & 0x80) == 0)
				{
					// This is a data byte, not a status byte - use running status
					reader.BaseStream.Position--; // Step back to re-read as data
					statusByte = runningStatus;
				}
				else
					runningStatus = statusByte;
				byte eventType = (byte)(statusByte & 0xF0),
					channel = (byte)(statusByte & 0x0F);
				switch (eventType)
				{
					case 0x80: // Note Off
						{
							byte note = reader.ReadByte();
							byte velocity = reader.ReadByte();
							midi.Events.Add(new NoteOffEvent
							{
								DeltaTime = deltaTime,
								Channel = channel,
								Note = note,
								Velocity = velocity
							});
						}
						break;
					case 0x90: // Note On
						{
							byte note = reader.ReadByte();
							byte velocity = reader.ReadByte();
							midi.Events.Add(new NoteOnEvent
							{
								DeltaTime = deltaTime,
								Channel = channel,
								Note = note,
								Velocity = velocity
							});
						}
						break;
					case 0xB0: // Controller Change
						{
							byte controller = reader.ReadByte(),
								value = reader.ReadByte();
							midi.Events.Add(new ControllerChangeEvent
							{
								DeltaTime = deltaTime,
								Channel = channel,
								Controller = controller,
								Value = value
							});
						}
						break;
					case 0xC0: // Program Change
						{
							byte program = reader.ReadByte();
							midi.Events.Add(new ProgramChangeEvent
							{
								DeltaTime = deltaTime,
								Channel = channel,
								Program = program
							});
						}
						break;
					case 0xD0: // Channel Pressure (Aftertouch)
						{
							byte pressure = reader.ReadByte();
							midi.Events.Add(new ChannelPressureEvent
							{
								DeltaTime = deltaTime,
								Channel = channel,
								Pressure = pressure
							});
						}
						break;
					case 0xE0: // Pitch Bend
						{
							byte lsb = reader.ReadByte(),
								msb = reader.ReadByte();
							midi.Events.Add(new PitchBendEvent
							{
								DeltaTime = deltaTime,
								Channel = channel,
								Value = (ushort)(msb << 7 | lsb)
							});
						}
						break;
					case 0xF0: // System messages
						if (statusByte == 0xFF) // Meta event
						{
							byte metaType = reader.ReadByte();
							uint metaLength = ReadVariableLength(reader);
							if (metaType == 0x51 && metaLength == 3) // Set Tempo
							{
								byte[] tempoBytes = reader.ReadBytes(3);
								uint microsecondsPerQuarterNote = (uint)(tempoBytes[0] << 16 | tempoBytes[1] << 8 | tempoBytes[2]);
								midi.Events.Add(new SetTempoEvent
								{
									DeltaTime = deltaTime,
									MicrosecondsPerQuarterNote = microsecondsPerQuarterNote
								});
							}
							else if (metaType == 0x2F) // End of Track
							{
								// Skip end of track marker
								reader.ReadBytes((int)metaLength);
								break; // Exit track parsing loop
							}
							else
							{
								// Skip unknown meta events
								reader.ReadBytes((int)metaLength);
							}
						}
						else if (statusByte == 0xF0 || statusByte == 0xF7) // SysEx
						{
							uint sysexLength = ReadVariableLength(reader);
							reader.ReadBytes((int)sysexLength); // Skip SysEx data
						}
						break;
					default:
						throw new InvalidDataException($"Unknown MIDI event type: 0x{eventType:X2}");
				}
			}
		}
		return midi;
	}
	/// <summary>
	/// Reads a MIDI variable-length value.
	/// ID_SD.C:2044-2052 MIDI_VarLength() - returns longword
	/// </summary>
	/// <returns>longword (C# uint)</returns>
	private static uint ReadVariableLength(BinaryReader reader)
	{
		uint value = 0;
		byte b;
		do
		{
			b = reader.ReadByte();
			value = value << 7 | (uint)(b & 0x7F);
		} while ((b & 0x80) != 0);
		return value;
	}
	/// <summary>
	/// Base class for all MIDI events.
	/// </summary>
	public abstract class MidiEvent
	{
		// ID_SD.C:2417 midiDeltaTime (variable-length MIDI time value)
		public uint DeltaTime { get; set; }  // longword in C (can exceed 16-bit)
	}
	/// <summary>
	/// MIDI Note Off event (0x80).
	/// ID_SD.C:2087-2111 MIDI_NoteOff(byte channel, byte note, byte velocity)
	/// </summary>
	public class NoteOffEvent : MidiEvent
	{
		public byte Channel { get; set; }  // MIDI channel (0-15)
		public byte Note { get; set; }     // MIDI note number (0-127)
		public byte Velocity { get; set; } // Note off velocity (0-127)
	}
	/// <summary>
	/// MIDI Note On event (0x90).
	/// ID_SD.C:2113-2181 MIDI_NoteOn(byte channel, byte note, byte velocity)
	/// </summary>
	public class NoteOnEvent : MidiEvent
	{
		public byte Channel { get; set; }  // MIDI channel (0-15)
		public byte Note { get; set; }     // MIDI note number (0-127)
		public byte Velocity { get; set; } // Note on velocity (0-127, 0=note off)
	}
	/// <summary>
	/// MIDI Controller Change event (0xB0).
	/// ID_SD.C:2183-2189 MIDI_ControllerChange(byte channel, byte id, byte value)
	/// </summary>
	public class ControllerChangeEvent : MidiEvent
	{
		public byte Channel { get; set; }    // MIDI channel (0-15)
		public byte Controller { get; set; } // Controller number (0-127)
		public byte Value { get; set; }      // Controller value (0-127)
	}
	/// <summary>
	/// MIDI Program Change event (0xC0).
	/// ID_SD.C:2191-2257 MIDI_ProgramChange(byte channel, byte id)
	/// </summary>
	public class ProgramChangeEvent : MidiEvent
	{
		public byte Channel { get; set; } // MIDI channel (0-15)
		public byte Program { get; set; } // Program/instrument number (0-127)
	}
	/// <summary>
	/// MIDI Channel Pressure (Aftertouch) event (0xD0).
	/// ID_SD.C:2259-2262 MIDI_ChannelPressure(byte channel, byte id)
	/// </summary>
	public class ChannelPressureEvent : MidiEvent
	{
		public byte Channel { get; set; }  // MIDI channel (0-15)
		public byte Pressure { get; set; } // Pressure value (0-127)
	}
	/// <summary>
	/// MIDI Pitch Bend event (0xE0).
	/// Not implemented in original Noah's Ark code but included for completeness.
	/// </summary>
	public class PitchBendEvent : MidiEvent
	{
		public byte Channel { get; set; }  // MIDI channel (0-15)
		public ushort Value { get; set; }  // 14-bit value (0-16383, center=8192)
	}
	/// <summary>
	/// MIDI Set Tempo meta event (0xFF 0x51).
	/// ID_SD.C:261 tempo variable, updated during playback
	/// </summary>
	public class SetTempoEvent : MidiEvent
	{
		public uint MicrosecondsPerQuarterNote { get; set; } // Tempo in μs/quarter note
	}
}
