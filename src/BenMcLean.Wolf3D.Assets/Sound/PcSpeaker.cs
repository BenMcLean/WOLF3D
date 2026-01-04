using System.IO;

namespace BenMcLean.Wolf3D.Assets.Sound;

/// <summary>
/// Parses and stores the PC Speaker sound effect format.
/// PC Speaker sounds use PWM (Pulse Width Modulation) to approximate sounds
/// by changing the speaker frequency every 1/140th of a second.
/// </summary>
/// <remarks>
/// Based on ID_SD.H:PCSound and ID_SD.C:SDL_PCPlaySound
/// See also: Fabien Sanglard's "Game Engine Black Book: Wolfenstein 3D" - Audio section
/// </remarks>
public class PcSpeaker
{
	/// <summary>
	/// PC Speaker sound effects play back at 140 Hz.
	/// Each frequency byte is played for 1/140th of a second.
	/// </summary>
	/// <remarks>
	/// From audio.tex: "SDL_t0SlowAsmService when running at 140Hz, to play sound effects on the beeper via PWM"
	/// </remarks>
	public const double Hz = 1.0 / 140.0;
	/// <summary>
	/// The PIT (Programmable Interval Timer) clock frequency in Hz.
	/// This is the base frequency used to calculate speaker frequencies.
	/// </summary>
	/// <remarks>
	/// From audio.tex: "The PIT chip runs at 1.193182 MHz"
	/// Original C code: 1193180 (integer approximation)
	/// </remarks>
	public const int PitClockHz = 1193180;
	/// <summary>
	/// Frequency lookup multiplier.
	/// Each frequency byte is multiplied by this value before being used as a PIT divisor.
	/// </summary>
	/// <remarks>
	/// From ID_SD.C: "pcSoundLookup[i] = i * 60;"
	/// From audio.tex pwm.c: "int count = 60 * data_byte;"
	/// </remarks>
	public const byte FrequencyMultiplier = 60;
	/// <summary>
	/// Priority of this sound effect (higher priority sounds can interrupt lower priority ones).
	/// </summary>
	/// <remarks>
	/// From ID_SD.H:SoundCommon:priority (word = ushort)
	/// </remarks>
	public readonly ushort Priority;
	/// <summary>
	/// Frequency data - each byte represents a frequency index.
	/// A value of 0 means silence (speaker off).
	/// Non-zero values are converted to Hz using: PitClockHz / (value * FrequencyMultiplier)
	/// </summary>
	/// <remarks>
	/// From ID_SD.H:PCSound:data[1] (byte array)
	/// From ID_SD.C:SDL_PCService: "if (s) // We have a frequency!" vs "else // Time for some silence"
	/// </remarks>
	public readonly byte[] FrequencyData;
	/// <summary>
	/// Constructs a PC Speaker sound from a binary reader.
	/// </summary>
	/// <param name="stream">Stream containing PC Speaker sound data</param>
	/// <remarks>
	/// Format from ID_SD.H:
	/// - SoundCommon.length (longword = uint32)
	/// - SoundCommon.priority (word = uint16)
	/// - data[] (byte array of length 'length')
	/// </remarks>
	public PcSpeaker(Stream stream)
	{
		using BinaryReader binaryReader = new(stream);
		// WL_DEF.H: typedef unsigned long longword (32-bit)
		uint length = binaryReader.ReadUInt32();
		// WL_DEF.H: typedef unsigned word (16-bit in DOS)
		Priority = binaryReader.ReadUInt16();
		// Read frequency data
		FrequencyData = new byte[length];
		binaryReader.Read(FrequencyData, 0, FrequencyData.Length);
	}
	/// <summary>
	/// Converts a frequency byte value to Hz.
	/// Returns 0 for silence (when frequencyByte is 0).
	/// </summary>
	/// <param name="frequencyByte">Frequency index byte (0 = silence)</param>
	/// <returns>Frequency in Hz, or 0 for silence</returns>
	/// <remarks>
	/// From audio.tex pwm.c:
	/// "int count = 60 * data_byte;"
	/// "int frequency_hz = 1193180 / count;"
	///
	/// From ID_SD.C:SDL_PCService:
	/// "t = pcSoundLookup[s];" where "pcSoundLookup[i] = i * 60;"
	/// Then the timer is programmed with 't' as the divisor
	/// </remarks>
	public static float GetFrequencyHz(byte frequencyByte) =>
		frequencyByte == 0
			? 0f // Silence
			: (float)PitClockHz / (frequencyByte * FrequencyMultiplier);
	/// <summary>
	/// Gets the total duration of this sound in seconds.
	/// </summary>
	public double DurationSeconds => FrequencyData.Length * Hz;
}
