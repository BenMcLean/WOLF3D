namespace BenMcLean.Wolf3D.Simulator.Scripting;

/// <summary>
/// Script context for the action stage (gameplay).
/// Provides action-specific API (spawn actors, damage, etc.)
/// </summary>
public abstract class ActionScriptContext(Simulator simulator, RNG rng, GameClock gameClock) : IScriptContext
{
	protected readonly Simulator simulator = simulator;
	protected readonly RNG rng = rng;
	protected readonly GameClock gameClock = gameClock;

	public RNG RNG => rng;
	public GameClock GameClock => gameClock;

	// Shared API (implemented by derived classes for context-specific behavior)
	public abstract void PlayDigiSound(int soundId);
	public abstract void PlayMusic(int musicId);
	public abstract void StopMusic();

	// Action-specific API
	public abstract void SpawnActor(int type, int x, int y);
	public abstract void DespawnActor(int actorId);
	public abstract int GetPlayerHealth();
	public abstract int GetPlayerMaxHealth();
	public abstract void HealPlayer(int amount);
	public abstract void DamagePlayer(int amount);
	public abstract void GivePlayerAmmo(int weaponType, int amount);
	public abstract void GivePlayerKey(int keyColor);
	public abstract bool PlayerHasKey(int keyColor);
}

/// <summary>
/// Script context for bonus objects (pickups).
/// Sounds play globally (not positional).
/// </summary>
public class BonusScriptContext(
	Simulator simulator,
	RNG rng,
	GameClock gameClock,
	int bonusX,
	int bonusY) : ActionScriptContext(simulator, rng, gameClock)
{

	// For bonuses, sounds are global (player is standing right next to it)
	public override void PlayDigiSound(int soundId)
	{
		// TODO: Emit event for VR layer to play global sound
		// simulator.EmitPlaySoundEvent(soundId, isPositional: false);
	}

	public override void PlayMusic(int musicId)
	{
		// TODO: Emit event
	}

	public override void StopMusic()
	{
		// TODO: Emit event
	}

	// Action API stubs (to be implemented as simulator evolves)
	public override void SpawnActor(int type, int x, int y)
	{
		// TODO: Implement actor spawning
	}

	public override void DespawnActor(int actorId)
	{
		// TODO: Implement despawning
	}

	public override int GetPlayerHealth()
	{
		// TODO: Return actual player health
		return 100;
	}

	public override int GetPlayerMaxHealth()
	{
		return 100;
	}

	public override void HealPlayer(int amount)
	{
		// TODO: Implement healing
	}

	public override void DamagePlayer(int amount)
	{
		// TODO: Implement damage
	}

	public override void GivePlayerAmmo(int weaponType, int amount)
	{
		// TODO: Implement ammo
	}

	public override void GivePlayerKey(int keyColor)
	{
		// TODO: Implement keys
	}

	public override bool PlayerHasKey(int keyColor)
	{
		// TODO: Return actual key state
		return false;
	}
}
