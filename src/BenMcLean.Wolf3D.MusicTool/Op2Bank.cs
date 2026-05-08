using System.Text;

namespace BenMcLean.Wolf3D.MusicTool;

internal sealed class Op2Bank
{
	public const int MelodicCount = 128;
	public const int PercussionCount = 47;
	public const int TotalCount = MelodicCount + PercussionCount;
	private const string Signature = "#OPL_II#";

	public Op2Patch[] Patches { get; } = new Op2Patch[TotalCount];
	public string[] Names { get; } = new string[TotalCount];

	public Op2Bank()
	{
		for (int i = 0; i < TotalCount; i++)
		{
			Patches[i] = Op2Patch.Silent();
			Names[i] = string.Empty;
		}
	}

	public static Op2Bank CreateSilent()
	{
		Op2Bank bank = new();
		for (int i = 0; i < TotalCount; i++)
			bank.Names[i] = i < MelodicCount ? $"Unused Melodic {i:D3}" : $"Unused Percussion {i + 35:D3}";
		return bank;
	}

	public void Save(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		using FileStream stream = File.Create(path);
		using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: false);
		writer.Write(Encoding.ASCII.GetBytes(Signature));
		for (int i = 0; i < TotalCount; i++)
			Patches[i].Write(writer);
		for (int i = 0; i < TotalCount; i++)
		{
			byte[] nameBytes = new byte[32];
			Encoding.ASCII.GetBytes(Names[i][..Math.Min(31, Names[i].Length)]).CopyTo(nameBytes, 0);
			writer.Write(nameBytes);
		}
	}
}

internal sealed class Op2Patch
{
	public ushort Flags { get; init; }
	public byte FineTune { get; init; } = 128;
	public byte NoteNumber { get; init; }
	public Op2Voice Voice1 { get; init; } = new();
	public Op2Voice Voice2 { get; init; } = new();

	public static Op2Patch Silent() => new();

	public static Op2Patch FromFullOplRegisters(
		byte modChar,
		byte carChar,
		byte modScale,
		byte carScale,
		byte modAttack,
		byte carAttack,
		byte modSustain,
		byte carSustain,
		byte modWave,
		byte carWave,
		byte feedback,
		ushort flags = 0,
		byte fineTune = 128,
		byte noteNumber = 60,
		short noteOffset = 0) =>
		new()
		{
			Flags = flags,
			FineTune = fineTune,
			NoteNumber = noteNumber,
			Voice1 = Op2Voice.FromFullOplRegisters(
				modChar,
				carChar,
				modScale,
				carScale,
				modAttack,
				carAttack,
				modSustain,
				carSustain,
				modWave,
				carWave,
				feedback,
				noteOffset)
		};

	public void Write(BinaryWriter writer)
	{
		writer.Write(Flags);
		writer.Write(FineTune);
		writer.Write(NoteNumber);
		Voice1.Write(writer);
		Voice2.Write(writer);
	}
}

internal sealed class Op2Voice
{
	public byte ModChar { get; init; }
	public byte ModAttack { get; init; }
	public byte ModSustain { get; init; }
	public byte ModWave { get; init; }
	public byte ModScale { get; init; }
	public byte ModLevel { get; init; }
	public byte Feedback { get; init; }
	public byte CarChar { get; init; }
	public byte CarAttack { get; init; }
	public byte CarSustain { get; init; }
	public byte CarWave { get; init; }
	public byte CarScale { get; init; }
	public byte CarLevel { get; init; }
	public byte Reserved { get; init; }
	public short NoteOffset { get; init; }

	public static Op2Voice FromFullOplRegisters(
		byte modChar,
		byte carChar,
		byte modScale,
		byte carScale,
		byte modAttack,
		byte carAttack,
		byte modSustain,
		byte carSustain,
		byte modWave,
		byte carWave,
		byte feedback,
		short noteOffset = 0) =>
		new()
		{
			ModChar = modChar,
			ModAttack = modAttack,
			ModSustain = modSustain,
			ModWave = modWave,
			ModScale = (byte)(modScale & 0xC0),
			ModLevel = (byte)(modScale & 0x3F),
			Feedback = feedback,
			CarChar = carChar,
			CarAttack = carAttack,
			CarSustain = carSustain,
			CarWave = carWave,
			CarScale = (byte)(carScale & 0xC0),
			CarLevel = (byte)(carScale & 0x3F),
			NoteOffset = noteOffset
		};

	public void Write(BinaryWriter writer)
	{
		writer.Write(ModChar);
		writer.Write(ModAttack);
		writer.Write(ModSustain);
		writer.Write(ModWave);
		writer.Write(ModScale);
		writer.Write(ModLevel);
		writer.Write(Feedback);
		writer.Write(CarChar);
		writer.Write(CarAttack);
		writer.Write(CarSustain);
		writer.Write(CarWave);
		writer.Write(CarScale);
		writer.Write(CarLevel);
		writer.Write(Reserved);
		writer.Write(NoteOffset);
	}
}
