using BenMcLean.Wolf3D.Assets;
using NScumm.Core.Audio.OPL;
using System;

namespace BenMcLean.Wolf3D.Shared.OPL;

/// <summary>
/// MIDI playback engine for Super 3-D Noah's Ark.
/// Interprets MIDI events and translates them to Adlib OPL2 register writes in real-time.
/// Based on ID_SD.C from Super 3-D Noah's Ark source code.
/// </summary>
public class MidiSignaller : IAdlibSignaller
{
	private Midi midi;
	private int eventIndex; // C# loop index
	private uint currentTempo = 500000, // ID_SD.C:261 tempo (microseconds per quarter note, default 120 BPM)
		deltaTimeRemaining; // ID_SD.C:2417 midiDeltaTime (longword)
	/// <summary>
	/// ID_SD.C:2056 static word NoteTable[12]
	/// Maps MIDI semitone (0-11) to Adlib frequency value
	/// </summary>
	private static readonly ushort[] NoteTable =   // word[] in C
	{
		0x157, 0x16b, 0x181, 0x198, 0x1b0, 0x1ca, 0x1e5, 0x202,
		0x220, 0x241, 0x263, 0x287
	};
	/// <summary>
	/// ID_SD.C:2060-2075 static inst_t instrument[14]
	/// ID_SD.H:184-191 inst_t structure (11 bytes)
	/// Format: INTERLEAVED - [mChar, cChar, mScale, cScale, mAttack, cAttack, mSustain, cSustain, mWave, cWave, feedCon]
	/// </summary>
	private static readonly byte[][] Instruments = // inst_t[] in C (array of byte[11])
	[
		[0x21, 0x31, 0x4f, 0x00, 0xf2, 0xd2, 0x52, 0x73, 0x00, 0x00, 0x06], // 0
		[0x01, 0x31, 0x4f, 0x04, 0xf0, 0x90, 0xff, 0x0f, 0x00, 0x00, 0x06], // 1
		[0x31, 0x22, 0x10, 0x04, 0x83, 0xf4, 0x9f, 0x78, 0x00, 0x00, 0x0a], // 2
		[0x11, 0x31, 0x05, 0x00, 0xf9, 0xf1, 0x25, 0x34, 0x00, 0x00, 0x0a], // 3
		[0x31, 0x61, 0x1c, 0x80, 0x41, 0x92, 0x0b, 0x3b, 0x00, 0x00, 0x0e], // 4
		[0x21, 0x21, 0x19, 0x80, 0x43, 0x85, 0x8c, 0x2f, 0x00, 0x00, 0x0c], // 5
		[0x21, 0x24, 0x94, 0x05, 0xf0, 0x90, 0x09, 0x0a, 0x00, 0x00, 0x0a], // 6
		[0x21, 0xa2, 0x83, 0x8d, 0x74, 0x65, 0x17, 0x17, 0x00, 0x00, 0x07], // 7
		[0x01, 0x01, 0x00, 0x00, 0xff, 0xff, 0x07, 0x07, 0x00, 0x00, 0x07], // 8
		[0x10, 0x00, 0x00, 0x00, 0xd8, 0x87, 0x4a, 0x3c, 0x00, 0x00, 0x00], // 9 (drums)
		[0x00, 0x00, 0x11, 0x11, 0xfa, 0xfa, 0xb5, 0xb5, 0x00, 0x00, 0x00], // 10
		[0x00, 0x00, 0x00, 0x00, 0xf8, 0xf8, 0x88, 0xb5, 0x00, 0x00, 0x00], // 11
		[0x15, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01], // 12
		[0x21, 0x11, 0x4c, 0x00, 0xf1, 0xf2, 0x63, 0x72, 0x00, 0x00, 0xc0], // 13
	];
	/// <summary>
	/// Channel state tracking
	/// </summary>
	private readonly byte[] channelInstruments = new byte[16]; // Current instrument per channel
	private byte drums = 0; // ID_SD.C:2058 static byte drums (rhythm mode flags)
	public Midi Midi
	{
		get => midi;
		set
		{
			midi = value;
			eventIndex = 0;
			currentTempo = 500000; // Reset to default tempo
			deltaTimeRemaining = midi?.Events.Count > 0 ? midi.Events[0].DeltaTime : 0;
		}
	}
	public void Init(IOpl opl)
	{
		opl?.WriteReg(1, 32); // Enable OPL2 mode
							  // Initialize all melodic channels (0-8) with default instrument
		for (int channel = 0; channel < 9; channel++)
			ProgramChange(opl, (byte)channel, 0);
		// Initialize drum channel (channel 9)
		ProgramChange(opl, 9, 0);
		opl?.WriteReg(0xBD, 0x20); // Enable rhythm mode (ID_SD.C:2426)
		drums = 0;
	}
	public uint Update(IOpl opl)
	{
		if (midi is null)
			return 1; // No music loaded
		// Loop when song ends (like ImfSignaller does)
		if (eventIndex >= midi.Events.Count)
		{
			eventIndex = 0;
			deltaTimeRemaining = midi.Events.Count > 0 ? midi.Events[0].DeltaTime : 0;
			// Note: Don't reset currentTempo - tempo changes persist across loops
		}
		// Process all events with zero delta-time at current position
		while (eventIndex < midi.Events.Count && deltaTimeRemaining == 0)
		{
			Midi.MidiEvent evt = midi.Events[eventIndex];
			ProcessEvent(opl, evt);
			eventIndex++;
			if (eventIndex < midi.Events.Count)
				deltaTimeRemaining = midi.Events[eventIndex].DeltaTime;
		}
		// Convert MIDI delta-time to 700Hz ticks
		// Formula from ID_SD.C:2385-2386, 2430
		// midiTimeScale = (tempo / 274176.0) * 1.1
		// delay = deltaTime * midiTimeScale
		uint delay = ConvertDeltaTimeTo700HzTicks(deltaTimeRemaining);
		deltaTimeRemaining = 0;
		return Math.Max(1, delay);
	}
	public void Silence(IOpl opl)
	{
		// Turn off all notes on all channels
		for (int channel = 0; channel < 9; channel++)
			opl?.WriteReg((byte)(0xB0 + channel), 0); // Key off
		opl?.WriteReg(0xBD, 0); // Disable rhythm mode
		drums = 0;
	}
	private void ProcessEvent(IOpl opl, Midi.MidiEvent evt)
	{
		switch (evt)
		{
			case Midi.NoteOnEvent noteOn:
				if (noteOn.Velocity > 0)
					NoteOn(opl, noteOn.Channel, noteOn.Note, noteOn.Velocity);
				else
					NoteOff(opl, noteOn.Channel, noteOn.Note, noteOn.Velocity);
				break;
			case Midi.NoteOffEvent noteOff:
				NoteOff(opl, noteOff.Channel, noteOff.Note, noteOff.Velocity);
				break;
			case Midi.ProgramChangeEvent programChange:
				ProgramChange(opl, programChange.Channel, programChange.Program);
				break;
			case Midi.ControllerChangeEvent controllerChange:
				// Controller changes not implemented in original (ID_SD.C:2183-2189)
				break;
			//case Midi.ChannelPressureEvent channelPressure:
			//	// Channel pressure not implemented in original (ID_SD.C:2259-2262)
			//	break;
			case Midi.SetTempoEvent setTempo:
				currentTempo = setTempo.MicrosecondsPerQuarterNote;
				break;
		}
	}
	/// <summary>
	/// Converts MIDI delta-time to 700Hz tick count based on current tempo.
	/// ID_SD.C:261 tempo variable
	/// ID_SD.C:2385-2386 midiTimeScale calculation
	/// ID_SD.C:2430 midiDeltaTime = midiDeltaTime * midiTimeScale
	/// </summary>
	/// <param name="deltaTime">MIDI delta-time (longword in C)</param>
	/// <returns>Number of 700Hz ticks to wait</returns>
	private uint ConvertDeltaTimeTo700HzTicks(uint deltaTime)
	{
		if (deltaTime == 0 || midi is null)
			return 0;
		// ID_SD.C:2385-2386
		// midiTimeScale = (tempo / 274176.0) * 1.1
		// The constant 274176.0 = 700Hz * 60s * 1000000μs / (some scaling factor)
		// delay_in_700Hz_ticks = deltaTime * midiTimeScale
		double timeScale = (currentTempo / 274176.0) * 1.1,
			ticks = deltaTime * timeScale;
		return (uint)Math.Max(1, Math.Round(ticks));
	}
	/// <summary>
	/// MIDI Note On handler.
	/// ID_SD.C:2113-2181 MIDI_NoteOn(int channel, byte note, byte velocity)
	/// Original uses int (16-bit) for channel, but MIDI channels are 0-15 so byte is semantically appropriate
	/// ID_SD.C:2056 NoteTable[12] for frequency mapping
	/// </summary>
	private void NoteOn(IOpl opl, byte channel, byte note, byte velocity)
	{
		if (channel == 9) // Drum channel (ID_SD.C:2126-2147)
		{
			// ID_SD.C:2128-2143 - Map specific MIDI drum notes to Adlib drum bits
			switch (note)
			{
				case 0x23: // 35
				case 0x24: // 36
					drums |= 0x10; // Bass drum
					break;
				case 0x26: // 38
					drums |= 0x08; // Snare
					break;
				case 0x28: // 40
					drums |= 0x04; // Hi-hat
					break;
				case 0x2a: // 42
					drums |= 0x01; // Tom
					break;
				default:
					return; // midiError = -11 in original
			}
			opl?.WriteReg(0xBD, (byte)(0x20 | drums)); // alOut(alEffects, alChar|drums)
			return;
		}
		// Original code uses channel directly without remapping
		// Channels >= 9 are silently ignored (write to invalid registers)
		if (channel >= 9)
			return;
		// Extract octave and semitone from MIDI note number
		int octave = (note / 12) - 1,
			semitone = note % 12;
		if (octave < 0 || octave > 7 || semitone >= NoteTable.Length)
			return;
		// Get frequency value from note table (ID_SD.C:2163-2164)
		ushort frequency = NoteTable[semitone];
		// Write frequency low byte (ID_SD.C:2166)
		opl?.WriteReg((byte)(0xA0 + channel), (byte)(frequency & 0xFF));
		// Write octave and frequency high bits, with key-on bit (ID_SD.C:2167-2168)
		byte blockFreq = (byte)(((octave & 0x07) << 2) | ((frequency >> 8) & 0x03) | 0x20);
		opl?.WriteReg((byte)(0xB0 + channel), blockFreq);
	}
	/// <summary>
	/// MIDI Note Off handler.
	/// ID_SD.C:2087-2111 MIDI_NoteOff(int channel, byte note, byte velocity)
	/// Original uses int (16-bit) for channel, but MIDI channels are 0-15 so byte is semantically appropriate
	/// </summary>
	private void NoteOff(IOpl opl, byte channel, byte note, byte velocity)
	{
		if (channel == 9) // Drum channel (ID_SD.C:2091-2105)
		{
			// ID_SD.C:2093-2104 - Clear specific drum bits
			switch (note)
			{
				case 0x23: // 35
				case 0x24: // 36
					drums &= 0xef; // Clear bass drum (bit 4)
					break;
				case 0x26: // 38
				case 0x28: // 40
					drums &= 0xf7; // Clear snare (bit 3)
					break;
				case 0x2a: // 42
					drums &= 0xfe; // Clear tom (bit 0)
					break;
				default:
					return;
			}
			opl?.WriteReg(0xBD, (byte)(0x20 | drums)); // Update rhythm register
			return;
		}
		if (channel >= 9)
			return;
		// Turn off key-on bit while preserving frequency/octave (ID_SD.C:2109)
		byte currentValue = 0; // In real implementation, we'd read current register value
		opl?.WriteReg((byte)(0xB0 + channel), (byte)(currentValue & ~0x20));
	}
	/// <summary>
	/// MIDI Program Change handler.
	/// ID_SD.C:2191-2257 MIDI_ProgramChange(int channel, int id)
	/// Original uses int (16-bit) for both parameters
	/// Using byte for MIDI semantics (channels 0-15, programs 0-127)
	/// ID_SD.H:184-191 inst_t structure definition
	/// </summary>
	private void ProgramChange(IOpl opl, byte channel, byte program)
	{
		if (channel >= 16)
			return;
		channelInstruments[channel] = program;
		if (channel == 9) // Drum channel special setup (ID_SD.C:2172-2242)
		{
			// ID_SD.C:2178-2190 - Configure bass drum (channel 6) using instrument[9]
			byte[] inst9 = Instruments[9];
			opl?.WriteReg(0x30, inst9[0]); // modChar
			opl?.WriteReg(0x33, inst9[1]); // carChar
			opl?.WriteReg(0x50, inst9[2]); // modScale
			opl?.WriteReg(0x53, inst9[3]); // carScale
			opl?.WriteReg(0x70, inst9[4]); // modAttack
			opl?.WriteReg(0x73, inst9[5]); // carAttack
			opl?.WriteReg(0x90, inst9[6]); // modSus
			opl?.WriteReg(0x93, inst9[7]); // carSus
			opl?.WriteReg(0xF0, inst9[8]); // modWave
			opl?.WriteReg(0xF3, inst9[9]); // carWave
			opl?.WriteReg(0xC6, inst9[10]); // feedCon
			// ID_SD.C:2192-2206 - Set frequencies for channels 6, 7, 8 to note 24
			byte note = 24;
			ushort fnumber = NoteTable[note % 12];
			byte octave = (byte)(((note / 12) & 7) << 2);
			opl?.WriteReg(0xA6, (byte)(fnumber & 0xFF));
			opl?.WriteReg(0xB6, (byte)(octave + ((fnumber >> 8) & 3)));
			opl?.WriteReg(0xA7, (byte)(fnumber & 0xFF));
			opl?.WriteReg(0xB7, (byte)(octave + ((fnumber >> 8) & 3)));
			opl?.WriteReg(0xA8, (byte)(fnumber & 0xFF));
			opl?.WriteReg(0xB8, (byte)(octave + ((fnumber >> 8) & 3)));
			// ID_SD.C:2208-2215 - Configure using instrument[10] (hi-hat modulator)
			byte[] inst10 = Instruments[10];
			opl?.WriteReg(0x31, inst10[0]); // Read interleaved: mChar
			opl?.WriteReg(0x51, inst10[2]); // mScale (skip cChar at [1])
			opl?.WriteReg(0x71, inst10[4]); // mAttack
			opl?.WriteReg(0x91, inst10[6]); // mSus
			opl?.WriteReg(0xF1, inst10[8]); // mWave
			opl?.WriteReg(0xC7, 0);
			// ID_SD.C:2217-2223 - Configure using instrument[12] (tom modulator)
			byte[] inst12 = Instruments[12];
			opl?.WriteReg(0x32, inst12[0]); // mChar
			opl?.WriteReg(0x52, inst12[2]); // mScale
			opl?.WriteReg(0x72, inst12[4]); // mAttack
			opl?.WriteReg(0x92, inst12[6]); // mSus
			opl?.WriteReg(0xF2, inst12[8]); // mWave
			// ID_SD.C:2225-2232 - Configure using instrument[11] (snare carrier)
			byte[] inst11 = Instruments[11];
			opl?.WriteReg(0x34, inst11[1]); // cChar (skip mChar at [0])
			opl?.WriteReg(0x54, inst11[3]); // cScale
			opl?.WriteReg(0x74, inst11[5]); // cAttack
			opl?.WriteReg(0x94, inst11[7]); // cSus
			opl?.WriteReg(0xF4, inst11[9]); // cWave
			opl?.WriteReg(0xC8, 0);
			// ID_SD.C:2234-2240 - Configure using instrument[10] again (cymbal carrier)
			opl?.WriteReg(0x35, inst10[1]); // cChar
			opl?.WriteReg(0x55, inst10[3]); // cScale
			opl?.WriteReg(0x75, inst10[5]); // cAttack
			opl?.WriteReg(0x95, inst10[7]); // cSus
			opl?.WriteReg(0xF5, inst10[9]); // cWave
			return;
		}
		// Only 9 melodic channels
		// ID_SD.C:2245 - Original only handles channels 0-4!
		// Channels 5-8 keep their default instrument from Init()
		if (channel >= 5)
			return;
		// ID_SD.C:2247-2288 Map program ID to instrument using exact switch statement
		// The original uses (id & 0xF8) to group programs into ranges of 8
		int instrumentIndex;
		switch (program & 0xF8)
		{
			case 0: instrumentIndex = 0; break;
			case 8: instrumentIndex = 8; break;
			case 16: instrumentIndex = 1; break;
			case 24: instrumentIndex = 0; break;
			case 32: instrumentIndex = 2; break;
			case 40:
			case 48: instrumentIndex = 0; break;
			case 56:
			case 64: instrumentIndex = 6; break;
			case 72: instrumentIndex = 7; break;
			case 80:
			case 88:
			case 96: instrumentIndex = 0; break;
			case 104:
			case 112:
			case 120: instrumentIndex = 8; break;
			default: return; // midiError = -8 in original
		}
		byte[] instrument = Instruments[instrumentIndex];    // inst_t (11 bytes)
		// ID_SD.C:2290-2301 Write instrument data using modifiers/carriers arrays
		// Channel offset mapping: 0,1,2,3,4,5,6,7,8 -> 0,1,2,8,9,10,16,17,18
		byte channelOffset = (byte)(channel < 3 ? channel : channel + 5);
		// Configure modulator and carrier (INTERLEAVED format)
		// ID_SD.C uses: modifiers[channel+1]+alChar, carriers[channel+1]+alChar, etc.
		opl?.WriteReg((byte)(0x20 + channelOffset), instrument[0]); // modChar
		opl?.WriteReg((byte)(0x23 + channelOffset), instrument[1]); // carChar
		opl?.WriteReg((byte)(0x40 + channelOffset), instrument[2]); // modScale
		opl?.WriteReg((byte)(0x43 + channelOffset), instrument[3]); // carScale
		opl?.WriteReg((byte)(0x60 + channelOffset), instrument[4]); // modAttack
		opl?.WriteReg((byte)(0x63 + channelOffset), instrument[5]); // carAttack
		opl?.WriteReg((byte)(0x80 + channelOffset), instrument[6]); // modSus
		opl?.WriteReg((byte)(0x83 + channelOffset), instrument[7]); // carSus
		opl?.WriteReg((byte)(0xE0 + channelOffset), instrument[8]); // modWave
		opl?.WriteReg((byte)(0xE3 + channelOffset), instrument[9]); // carWave
		// Configure feedback/connection (ID_SD.C:2301)
		opl?.WriteReg((byte)(0xC0 + channel), instrument[10]);
	}
}
