using System;
using Godot;

namespace BenMcLean.Wolf3D.Shared.Audio;

public partial class PcSpeakerPlayer : AudioStreamPlayer
{
	public const ushort MixRate = 44100,
		PcFramesPerUpdate = 315; // 44100 / 140 Hz = ~315 frames
	private Assets.Sound.PcSpeaker currentPcSound = null;
	private int pcSampleIndex = 0;
	private double pcPhase = 0.0;
	private int pcFramesInCurrentSample = 0;
	public PcSpeakerPlayer()
	{
		Stream = new AudioStreamGenerator()
		{
			MixRate = MixRate,
			BufferLength = 0.05f,
		};
	}
	public void PlaySound(Assets.Sound.PcSpeaker sound)
	{
		if (sound is null || SharedAssetManager.Config?.SoundMode != Assets.Gameplay.Config.SDMode.PC)
			return;
		currentPcSound = sound;
		pcSampleIndex = 0;
		pcPhase = 0f;
		pcFramesInCurrentSample = 0;
		if (!Playing)
			Play();
	}
	public void StopSound()
	{
		currentPcSound = null;
		pcSampleIndex = 0;
		pcPhase = 0f;
		pcFramesInCurrentSample = 0;
	}
	public override void _Process(double delta)
	{
		// Early exit if PC Speaker is not active - avoid unnecessary processing
		if (SharedAssetManager.Config?.SoundMode is not Assets.Gameplay.Config.SDMode.PC ||
			currentPcSound is null)
			return;
		if (!Playing || GetStreamPlayback() is not AudioStreamGeneratorPlayback playback)
			return;
		int framesToFill = playback.GetFramesAvailable();
		if (framesToFill <= 0)
			return;
		Vector2[] buffer = new Vector2[framesToFill];
		int bufferPos = 0;
		while (bufferPos < framesToFill && pcSampleIndex < currentPcSound.FrequencyData.Length)
		{
			// Get current frequency for this 1/140th second slice
			byte frequencyByte = currentPcSound.FrequencyData[pcSampleIndex];
			float frequencyHz = Assets.Sound.PcSpeaker.GetFrequencyHz(frequencyByte);
			// Calculate how many frames to generate for this frequency
			int framesToGenerate = Math.Min(
				PcFramesPerUpdate - pcFramesInCurrentSample,
				framesToFill - bufferPos);
			// Generate square wave samples
			for (int i = 0; i < framesToGenerate; i++)
			{
				float sample;
				if (frequencyHz == 0f)
				{
					// Silence (speaker off)
					sample = 0f;
					pcPhase = 0f; // Reset phase during silence
				}
				else
				{
					// Generate square wave (alternates between -1 and +1)
					sample = pcPhase < MathF.PI ? 1f : -1f;
					// Advance phase
					float phaseIncrement = 2f * MathF.PI * frequencyHz / MixRate;
					pcPhase += phaseIncrement;
					// Wrap phase to [0, 2*PI]
					if (pcPhase >= 2f * MathF.PI)
						pcPhase -= 2f * MathF.PI;
				}
				// Scale down the volume (PC Speaker was quieter)
				// and convert mono to stereo
				sample *= 0.3f;
				buffer[bufferPos++] = new Vector2(sample, sample);
				pcFramesInCurrentSample++;
			}
			// Check if we've generated enough frames for this frequency byte
			if (pcFramesInCurrentSample >= PcFramesPerUpdate)
			{
				pcSampleIndex++;
				pcFramesInCurrentSample = 0;
			}
		}
		// Push generated samples to the playback buffer
		if (bufferPos > 0)
			if (bufferPos < buffer.Length)
			{
				Vector2[] trimmedBuffer = new Vector2[bufferPos];
				Array.Copy(buffer, trimmedBuffer, bufferPos);
				playback.PushBuffer(trimmedBuffer);
			}
			else
				playback.PushBuffer(buffer);
		// Check if sound has finished playing
		if (pcSampleIndex >= currentPcSound.FrequencyData.Length)
			StopSound();
	}
}
