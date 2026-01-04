using BenMcLean.Wolf3D.Shared.Audio.OPL;
using Godot;
using NScumm.Core.Audio.OPL;

namespace BenMcLean.Wolf3D.Shared.Audio.OPL;

public partial class OplPlayer : AudioStreamPlayer
{
	public const ushort MixRate = 44100;
	public const byte FramesPerUpdate = 63; // 700 Hz interval
	public OplPlayer()
	{
		Name = "OplPlayer";
		Stream = new AudioStreamGenerator()
		{
			MixRate = MixRate,
			BufferLength = 0.05f, // Keep this as short as possible to minimize latency
		};
	}
	public IAdlibSignaller AdlibSignaller
	{
		get => adlibSignaller;
		set
		{
			if ((adlibSignaller = value) is not null && Opl is not null)
				AdlibSignaller.Init(Opl);
		}
	}
	private IAdlibSignaller adlibSignaller = null;
	public IOpl Opl
	{
		get => opl;
		set
		{
			if ((opl = value) is not null)
			{
				Opl?.Init(MixRate);
				AdlibSignaller?.Init(Opl);
			}
		}
	}
	private IOpl opl;
	public override void _Ready() => Play();
	public override void _Process(double delta)
	{
		if (Playing && AdlibSignaller is not null)
			if (Opl is null)
				Stop();
			else
				FillBuffer();
	}
	public OplPlayer FillBuffer()
	{
		if (Opl is null)
			return this;
		int toFill = ((AudioStreamGeneratorPlayback)GetStreamPlayback()).GetFramesAvailable();
		if (ShortBuffer is null || ShortBuffer.Length < toFill)
			ShortBuffer = new short[toFill];
		int pos = 0;
		while (LeftoverFrames > 0 && pos + LeftoverFrames < toFill && LeftoverFrames > FramesPerUpdate)
		{
			Opl.ReadBuffer(ShortBuffer, pos, FramesPerUpdate);
			pos += FramesPerUpdate;
			LeftoverFrames -= FramesPerUpdate;
		}
		if (LeftoverFrames > 0 && pos + LeftoverFrames < toFill)
		{
			Opl.ReadBuffer(ShortBuffer, pos, LeftoverFrames);
			pos += LeftoverFrames;
			LeftoverFrames = 0;
		}
		while (pos + LeftoverFrames < toFill)
		{
			LeftoverFrames += (int)AdlibSignaller.Update(Opl) * FramesPerUpdate;
			if (pos + LeftoverFrames < toFill)
			{
				Opl.ReadBuffer(ShortBuffer, pos, LeftoverFrames);
				pos += LeftoverFrames;
				LeftoverFrames = 0;
			}
		}
		if (pos > 0)
		{
			Vector2[] vector2Buffer = new Vector2[pos];
			for (uint i = 0; i < vector2Buffer.Length; i++)
			{
				float soundbite = ShortBuffer[i] / 32767f; // Convert from 16 bit signed integer audio to 32 bit signed float audio
				vector2Buffer[i] = new Vector2(soundbite, soundbite); // Convert mono to stereo
			}
			if (vector2Buffer.Length > 0)
				((AudioStreamGeneratorPlayback)GetStreamPlayback()).PushBuffer(vector2Buffer);
		}
		return this;
	}
	private short[] ShortBuffer;
	private int LeftoverFrames = 0;
}
