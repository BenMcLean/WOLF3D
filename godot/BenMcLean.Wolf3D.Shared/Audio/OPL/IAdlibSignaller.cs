using NScumm.Core.Audio.OPL;

namespace BenMcLean.Wolf3D.Shared.Audio.OPL;

public interface IAdlibSignaller
{
	void Init(IOpl opl);
	/// <returns>The number of 700 Hz intervals to wait until calling Update again</returns>
	uint Update(IOpl opl);
	void Silence(IOpl opl);
}
