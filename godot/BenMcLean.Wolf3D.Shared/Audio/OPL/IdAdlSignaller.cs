using BenMcLean.Wolf3D.Assets.Sound;
using NScumm.Core.Audio.OPL;
using System.Collections.Concurrent;

namespace BenMcLean.Wolf3D.Shared.Audio.OPL;

/// <summary>
/// id Software Adlib Sound Effect Player by Ben McLean mclean.ben@gmail.com
/// </summary>
public class IdAdlSignaller : IAdlibSignaller
{
	public void Init(IOpl opl) => opl?.WriteReg(1, 32); // go to OPL2 mode
	public void Silence(IOpl opl) => SetNote(false, opl);
	public bool Note { get; private set; }
	public IdAdlSignaller SetNote(bool value, IOpl opl)
	{
		if (Note = value)
			opl?.WriteReg(Adl.OctavePort, (byte)(Adl.Block | Adl.KeyFlag));
		else
			opl?.WriteReg(Adl.OctavePort, 0);
		return this;
	}
	public IdAdlSignaller SetInstrument(IOpl opl)
	{
		opl.WriteReg(1, 32); // go to OPL2 mode
		for (int i = 0; i < Adl.InstrumentPorts.Count; i++)
			opl?.WriteReg(Adl.InstrumentPorts[i], Adl.Instrument[i]);
		opl?.WriteReg(0xC0, 0); // WOLF3D's code ignores this value in its sound data, always setting it to zero instead.
		return this;
	}
	public static readonly ConcurrentQueue<Adl> IdAdlQueue = new();
	public uint Update(IOpl opl)
	{
		if (IdAdlQueue.TryDequeue(out Adl adl)
			&& (Adl is null || adl is null || Adl == adl || adl.Priority >= Adl.Priority))
		{
			CurrentNote = 0;
			if (opl is not null)
			{
				SetNote(false, opl); // Must send a signal to stop the previous sound before starting a new sound
				if ((Adl = adl) is not null)
				{
					SetInstrument(opl);
					SetNote(true, opl);
				}
			}
		}
		if (Adl is not null)
		{
			if (Adl.Notes[CurrentNote] == 0)
				SetNote(false, opl);
			else
			{
				if (!Note) SetNote(true, opl);
				opl?.WriteReg(Adl.NotePort, Adl.Notes[CurrentNote]);
			}
			CurrentNote++;
			if (CurrentNote >= Adl.Notes.Length)
			{
				Adl = null;
				SetNote(false, opl);
			}
		}
		return 5; // These sound effects play back at 140 Hz.
	}
	public uint CurrentNote = 0;
	public Adl Adl { get; private set; } = null;
}
