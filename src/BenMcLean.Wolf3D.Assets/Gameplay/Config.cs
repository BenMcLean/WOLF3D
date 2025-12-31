using System;
using System.IO;
using System.Linq;
using System.Text;

namespace BenMcLean.Wolf3D.Assets.Gameplay;

/// <summary>
/// Represents a Wolfenstein 3-D family CONFIG file (e.g., CONFIG.WL6, CONFIG.N3D).
/// Contains high scores, sound settings, input configuration, and control mappings.
/// Follows the classic binary format exactly when reading/writing, but uses
/// efficient types internally.
/// </summary>
public sealed class Config
{
	public const int MaxScores = 7,
		HighScoresSize = MaxScores * HighScoreEntry.EntrySize; // 462 bytes
	#region Data
	/// <summary>
	/// The format variant this config uses. Determines array sizes and optional fields.
	/// </summary>
	public ConfigFormat Format { get; set; }
	public HighScoreEntry[] Scores { get; set; }
	#region Sound Settings
	public SDMode SoundMode { get; set; }
	public bool MusicEnabled { get; set; }
	public SDSMode DigiMode { get; set; }
	#endregion Sound Settings
	#region Input Device Settings
	public bool MouseEnabled { get; set; }
	public bool JoystickEnabled { get; set; }
	public bool JoypadEnabled { get; set; }
	public bool JoystickProgressive { get; set; }
	public short JoystickPort { get; set; }
	#endregion Input Device Settings
	#region Control Mappings
	public short[] DirScan { get; set; } = new short[4];
	public short[] ButtonScan { get; set; }
	public short[] ButtonMouse { get; set; } = new short[4];
	public short[] ButtonJoy { get; set; } = new short[4];
	#endregion Control Mappings
	#region View Settings
	public short ViewSize { get; set; }
	public short MouseAdjustment { get; set; }
	#endregion View Settings
	#region Game-Specific Settings
	private short _questionNum;
	/// <summary>
	/// Noah's Ark 3D only: tracks which trivia question to ask next (0-98).
	/// Only used when Format == ConfigFormat.NoahsArk.
	/// Value wraps around: 99 → 0, -1 → 98, etc.
	/// </summary>
	public short QuestionNum
	{
		get => _questionNum;
		set => _questionNum = (short)((value % 99 + 99) % 99);
	}
	#endregion Game Specific Settings
	#endregion Data
	public Config(ConfigFormat format = ConfigFormat.Wolf3D)
	{
		Format = format;
		Scores = new HighScoreEntry[MaxScores];
		for (int i = 0; i < MaxScores; i++)
			Scores[i] = new HighScoreEntry();
		// Initialize ButtonScan with correct size for format
		ButtonScan = new short[format == ConfigFormat.NoahsArk ? 10 : 8];
	}
	/// <summary>
	/// Loads a complete CONFIG file from a stream.
	/// </summary>
	/// <param name="stream">Stream positioned at the start of the config data</param>
	/// <param name="format">The game variant format to use</param>
	public static Config Load(Stream stream, ConfigFormat format = ConfigFormat.Wolf3D)
	{
		Config config = new(format);
		using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
		#region Read high scores (462 bytes)
		for (int i = 0; i < MaxScores; i++)
		{
			byte[] nameBytes = reader.ReadBytes(HighScoreEntry.MaxHighName + 1);
			string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
			int score = reader.ReadInt32();
			ushort completed = reader.ReadUInt16();
			ushort episode = reader.ReadUInt16();
			config.Scores[i] = new HighScoreEntry
			{
				Name = name,
				Score = score,
				Completed = completed,
				Episode = episode
			};
		}
		#endregion Read high scores (462 bytes)
		#region Read sound settings (6 bytes)
		config.SoundMode = (SDMode)reader.ReadInt16();
		config.MusicEnabled = reader.ReadInt16() != 0; // SMMode: 0=Off, 1=AdLib
		config.DigiMode = (SDSMode)reader.ReadInt16();
		#endregion Read sound settings (6 bytes)
		#region Read input device settings (10 bytes)
		config.MouseEnabled = reader.ReadInt16() != 0;
		config.JoystickEnabled = reader.ReadInt16() != 0;
		config.JoypadEnabled = reader.ReadInt16() != 0;
		config.JoystickProgressive = reader.ReadInt16() != 0;
		config.JoystickPort = reader.ReadInt16();
		#endregion Read input device settings (10 bytes)
		#region Read control mappings
		for (int i = 0; i < 4; i++)
			config.DirScan[i] = reader.ReadInt16();
		int buttonCount = config.ButtonScan.Length;
		for (int i = 0; i < buttonCount; i++)
			config.ButtonScan[i] = reader.ReadInt16();
		for (int i = 0; i < 4; i++)
			config.ButtonMouse[i] = reader.ReadInt16();
		for (int i = 0; i < 4; i++)
			config.ButtonJoy[i] = reader.ReadInt16();
		#endregion Read control mappings
		#region Read view settings (4 bytes)
		config.ViewSize = reader.ReadInt16();
		config.MouseAdjustment = reader.ReadInt16();
		#endregion Read view settings (4 bytes)
		#region Read game-specific settings
		if (format == ConfigFormat.NoahsArk)
			config.QuestionNum = reader.ReadInt16();
		#endregion Read game-specific settings
		return config;
	}
	/// <summary>
	/// Saves the complete CONFIG file to a stream.
	/// </summary>
	/// <param name="stream">Stream positioned where the config data should be written</param>
	public void Save(Stream stream)
	{
		using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);
		#region Write high scores (462 bytes)
		for (int i = 0; i < MaxScores; i++)
		{
			HighScoreEntry entry = Scores[i];
			byte[] nameBytes = new byte[HighScoreEntry.MaxHighName + 1];
			byte[] sourceBytes = Encoding.ASCII.GetBytes(entry.Name ?? string.Empty);
			int copyLength = Math.Min(sourceBytes.Length, HighScoreEntry.MaxHighName);
			Array.Copy(sourceBytes, nameBytes, copyLength);
			writer.Write(nameBytes);
			writer.Write(entry.Score);
			writer.Write(entry.Completed);
			writer.Write(entry.Episode);
		}
		#endregion Write high scores (462 bytes)
		#region Write sound settings (6 bytes)
		writer.Write((short)SoundMode);
		writer.Write((short)(MusicEnabled ? 1 : 0)); // SMMode: 0=Off, 1=AdLib
		writer.Write((short)DigiMode);
		#endregion Write sound settings (6 bytes)
		#region Write input device settings (10 bytes)
		writer.Write((short)(MouseEnabled ? 1 : 0));
		writer.Write((short)(JoystickEnabled ? 1 : 0));
		writer.Write((short)(JoypadEnabled ? 1 : 0));
		writer.Write((short)(JoystickProgressive ? 1 : 0));
		writer.Write(JoystickPort);
		#endregion Write input device settings (10 bytes)
		#region Write control mappings
		for (int i = 0; i < 4; i++)
			writer.Write(DirScan[i]);
		int buttonCount = ButtonScan.Length;
		for (int i = 0; i < buttonCount; i++)
			writer.Write(ButtonScan[i]);
		for (int i = 0; i < 4; i++)
			writer.Write(ButtonMouse[i]);
		for (int i = 0; i < 4; i++)
			writer.Write(ButtonJoy[i]);
		#endregion Write control mappings
		#region Write view settings (4 bytes)
		writer.Write(ViewSize);
		writer.Write(MouseAdjustment);
		#endregion Write view settings (4 bytes)
		#region Write game-specific settings
		if (Format == ConfigFormat.NoahsArk)
			writer.Write(QuestionNum);
		#endregion Write game-specific settings
	}
	#region High Score Methods
	/// <summary>
	/// Determines if a given score would qualify for the high score table.
	/// </summary>
	public bool WouldQualify(HighScoreEntry entry) => ScoreIndex(entry) > -1;
	/// <summary>
	/// Finds the index where a high score entry should be inserted.
	/// Returns -1 if the entry doesn't beat any existing score.
	/// </summary>
	public int ScoreIndex(HighScoreEntry entry) => Array.FindIndex(Scores, s => entry > s);
	/// <summary>
	/// Adds a new high score to the table, maintaining sort order.
	/// Returns the index where the score was inserted, or -1 if it didn't qualify.
	/// </summary>
	public int AddScore(HighScoreEntry entry)
	{
		int insertIndex = ScoreIndex(entry);
		if (insertIndex == -1)
			return -1;
		Array.Copy(// Shift entries down
			sourceArray: Scores,
			sourceIndex: insertIndex,
			destinationArray: Scores,
			destinationIndex: insertIndex + 1,
			length: MaxScores - insertIndex - 1);
		Scores[insertIndex] = entry;
		return insertIndex;
	}
	#endregion High Score Methods
	#region Supporting Types
	/// <summary>
	/// Specifies which game variant's CONFIG file format to use.
	/// </summary>
	public enum ConfigFormat
	{
		/// <summary>Wolfenstein 3D (8 buttons, no QuestionNum)</summary>
		Wolf3D,
		/// <summary>Super 3D Noah's Ark (10 buttons, includes QuestionNum)</summary>
		NoahsArk,
	}
	/// <summary>
	/// Represents a single high score entry.
	///
	/// Comparison order:
	/// 1. Score (primary) - Higher scores rank better
	/// 2. Episode (secondary) - Later episodes assumed harder, so higher episodes rank better for same score
	/// 3. Completed (tertiary) - More levels completed ranks better
	/// 4. Exact ties (same score, episode, and completed) - First achiever ranks better (newer entries go below)
	/// </summary>
	public sealed class HighScoreEntry : IComparable<HighScoreEntry>
	{
		public const int MaxHighName = 57,
			EntrySize = 66; // 58 + 4 + 2 + 2
		private string _name = string.Empty;
		/// <summary>
		/// Player name (max 57 characters, ASCII only).
		/// Non-ASCII characters are replaced with '?', and the string is truncated if too long.
		/// </summary>
		public string Name
		{
			get => _name;
			set => _name = value is null ?
				string.Empty
				: new string([.. value
					.Select(c => c is >= (char)0 and <= (char)127 ? c : '?')
					.Take(MaxHighName)]);
		}
		public int Score { get; set; }
		public ushort Completed { get; set; }
		public ushort Episode { get; set; }
		#region IComparable<HighScoreEntry>
		public int CompareTo(HighScoreEntry other) => other is null ? 1 : (Score, Episode, Completed).CompareTo((other.Score, other.Episode, other.Completed));
		public static bool operator >(HighScoreEntry left, HighScoreEntry right) =>
			left is not null && (right is null || left.CompareTo(right) > 0);
		public static bool operator <(HighScoreEntry left, HighScoreEntry right) =>
			right is not null && (left is null || left.CompareTo(right) < 0);
		public static bool operator >=(HighScoreEntry left, HighScoreEntry right) =>
			left is null ? right is null : left.CompareTo(right) >= 0;
		public static bool operator <=(HighScoreEntry left, HighScoreEntry right) =>
			left is null || right is not null && left.CompareTo(right) <= 0;
		#endregion IComparable<HighScoreEntry>
	}
	/// <summary>
	/// Sound mode enumeration (SDMode).
	/// Stored as 2-byte int in binary format.
	/// </summary>
	public enum SDMode : byte
	{
		Off = 0,
		PC = 1,
		AdLib = 2,
	}
	/// <summary>
	/// Digital sound mode enumeration (SDSMode).
	/// Stored as 2-byte int in binary format.
	/// </summary>
	public enum SDSMode : byte
	{
		Off = 0,
		PC = 1,          // Not available in Noah's Ark
		SoundSource = 2, // Not available in Noah's Ark
		SoundBlaster = 3,
	}
	#endregion Supporting Types
}
