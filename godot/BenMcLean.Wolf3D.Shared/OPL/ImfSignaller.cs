using BenMcLean.Wolf3D.Assets.Sound;
using NScumm.Core.Audio.OPL;
using System.Collections.Concurrent;

namespace BenMcLean.Wolf3D.Shared.OPL;

public class ImfSignaller : IAdlibSignaller
{
	public void Init(IOpl opl) => opl?.WriteReg(1, 32); // go to OPL2 mode
	public int Position;
	public Imf[] Imf
	{
		get => imf;
		set
		{
			imf = value;
			Position = 0;
		}
	}
	private Imf[] imf = null;
	public static readonly ConcurrentQueue<Imf[]> ImfQueue = new();
	public uint Update(IOpl opl)
	{
		if (ImfQueue.TryDequeue(out Imf[] imf))
		{
			Silence(opl);
			Imf = imf;
		}
		if (Imf is null)
			return 1;
		ushort delay;
		do
		{
			opl?.WriteReg(Imf[Position].Register, Imf[Position].Data);
			delay = Imf[Position].Delay;
			Position++;
		} while (delay == 0 && Position < Imf.Length);
		if (Position >= Imf.Length)
		{
			Position = 0;
			return 1;
		}
		return delay;
	}
	public void Silence(IOpl opl)
	{
		opl?.WriteReg(189, 0);
		for (int i = 0; i < 10; i++)
			opl?.WriteReg(177 + i, 0);
	}
}
