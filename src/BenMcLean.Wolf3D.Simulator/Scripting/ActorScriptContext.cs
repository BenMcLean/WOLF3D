using System.Linq;
using BenMcLean.Wolf3D.Assets;
using static BenMcLean.Wolf3D.Assets.MapAnalyzer;

namespace BenMcLean.Wolf3D.Simulator.Scripting;

/// <summary>
/// Script context for actor Think and Action functions.
/// Provides actor-specific API for AI, movement, combat, and state transitions.
/// </summary>
public class ActorScriptContext(
	Simulator simulator,
	Actor actor,
	int actorIndex,
	RNG rng,
	GameClock gameClock,
	MapAnalysis mapAnalysis) : ActionScriptContext(simulator, rng, gameClock)
{
	private readonly Actor actor = actor;
	private readonly int actorIndex = actorIndex;
	private readonly MapAnalysis mapAnalysis = mapAnalysis;

	// Actor property accessors (read-only from Lua)

	/// <summary>Get actor's current tile X coordinate</summary>
	public int GetTileX() => actor.TileX;

	/// <summary>Get actor's current tile Y coordinate</summary>
	public int GetTileY() => actor.TileY;

	/// <summary>Get actor's fixed-point X coordinate</summary>
	public int GetX() => actor.X;

	/// <summary>Get actor's fixed-point Y coordinate</summary>
	public int GetY() => actor.Y;

	/// <summary>Get actor's current hit points</summary>
	public int GetHitPoints() => actor.HitPoints;

	/// <summary>Get actor's current facing direction (0-7)</summary>
	public int GetFacing() => (int)actor.Facing;

	/// <summary>Get actor's current speed</summary>
	public int GetSpeed() => actor.Speed;

	/// <summary>Get distance to player (updated each tic)</summary>
	public int GetDistanceToPlayer() => actor.DistanceToPlayer;

	/// <summary>WL_STATE.C: Get actor reaction timer (ob->temp2)</summary>
	public int GetReactionTimer() => actor.ReactionTimer;

	/// <summary>WL_STATE.C: Set actor reaction timer (ob->temp2)</summary>
	public void SetReactionTimer(int value) => actor.ReactionTimer = (short)value;

	/// <summary>Check if actor has a specific flag set</summary>
	public bool HasFlag(int flagValue) => (actor.Flags & (ActorFlags)flagValue) != 0;

	/// <summary>Set a flag on the actor</summary>
	public void SetFlag(int flagValue) => actor.Flags |= (ActorFlags)flagValue;

	/// <summary>Clear a flag on the actor</summary>
	public void ClearFlag(int flagValue) => actor.Flags &= ~(ActorFlags)flagValue;

	/// <summary>
	/// Get random number from 0 to 255 (matching US_RndT from original code).
	/// WL_DEF.H:US_RndT()
	/// </summary>
	public int Random() => rng.Next(256);

	/// <summary>
	/// Check line of sight to player using raycasting.
	/// WL_STATE.C:CheckSight(ob)
	/// Walks from actor to player tile-by-tile, checking MapAnalysis.IsTransparent.
	/// TODO: Eventually needs to check closed doors blocking sight (for now just uses IsTransparent)
	/// </summary>
	public bool CheckSight()
	{
		if (mapAnalysis == null)
		{
			// Fallback if no map analysis available
			return CalculateDistanceToPlayer() < 20;
		}

		int actorTileX = actor.TileX;
		int actorTileY = actor.TileY;
		int playerTileX = simulator.PlayerTileX;
		int playerTileY = simulator.PlayerTileY;

		// Bresenham's line algorithm to walk from actor to player
		int dx = System.Math.Abs(playerTileX - actorTileX);
		int dy = System.Math.Abs(playerTileY - actorTileY);
		int sx = actorTileX < playerTileX ? 1 : -1;
		int sy = actorTileY < playerTileY ? 1 : -1;
		int err = dx - dy;

		int x = actorTileX;
		int y = actorTileY;

		while (true)
		{
			// Reached player position - line of sight is clear
			if (x == playerTileX && y == playerTileY)
				return true;

			// Check if current tile blocks sight
			// Skip the actor's own tile (we start there)
			if (!(x == actorTileX && y == actorTileY))
			{
				if (!mapAnalysis.IsTransparent(x, y))
					return false; // Wall or obstacle blocks sight
			}

			// Bresenham step
			int e2 = 2 * err;
			if (e2 > -dy)
			{
				err -= dy;
				x += sx;
			}
			if (e2 < dx)
			{
				err += dx;
				y += sy;
			}
		}
	}

	// Actor mutation methods (for Think/Action scripts)

	/// <summary>
	/// Change the actor's current state.
	/// Triggers state transition with Action function execution.
	/// </summary>
	/// <param name="stateName">Name of the state to transition to</param>
	public void ChangeState(string stateName)
	{
		// Find this actor's index in the simulator
		int actorIndex = simulator.Actors.ToList().IndexOf(actor);
		if (actorIndex >= 0)
		{
			simulator.TransitionActorStateByName(actorIndex, stateName);
		}
	}

	/// <summary>
	/// Set actor's facing direction.
	/// </summary>
	/// <param name="direction">Direction value (0-7 for 8-way directions)</param>
	public void SetFacing(int direction)
	{
		if (direction >= 0 && direction <= 7)
			actor.Facing = (Direction)direction;
	}

	/// <summary>
	/// Move actor to a specific tile position.
	/// </summary>
	/// <param name="tileX">Target tile X coordinate</param>
	/// <param name="tileY">Target tile Y coordinate</param>
	public void MoveTo(int tileX, int tileY)
	{
		// TODO: Implement movement with collision detection
		// Should update actor.X, actor.Y, actor.TileX, actor.TileY
		// Should emit ActorMovedEvent if position changes
	}

	/// <summary>
	/// Turn actor to face a specific point.
	/// </summary>
	/// <param name="targetX">Target X coordinate (fixed-point)</param>
	/// <param name="targetY">Target Y coordinate (fixed-point)</param>
	public void TurnToward(int targetX, int targetY)
	{
		// Calculate direction from actor to target
		int dx = targetX - actor.X;
		int dy = targetY - actor.Y;
		// TODO: Calculate 8-way direction from dx/dy and set actor.Facing
	}

	/// <summary>
	/// Damage the actor.
	/// </summary>
	/// <param name="amount">Damage amount</param>
	public void TakeDamage(int amount)
	{
		actor.HitPoints -= (short)amount;
		// TODO: Check for death, trigger death state transition
	}

	/// <summary>
	/// Heal the actor.
	/// </summary>
	/// <param name="amount">Healing amount</param>
	public void Heal(int amount)
	{
		actor.HitPoints += (short)amount;
		// TODO: Clamp to max health
	}

	// Global simulator queries (for AI decisions)

	/// <summary>
	/// Check if actor has line-of-sight to the player.
	/// </summary>
	/// <returns>True if player is visible</returns>
	public bool CanSeePlayer()
	{
		// Simple implementation: check if player is within certain distance
		// TODO: Implement proper line-of-sight raycast checking for walls
		int dist = CalculateDistanceToPlayer();
		return dist < 15; // Within ~15 tiles
	}

	/// <summary>
	/// Get the player's current tile X coordinate.
	/// WL_DEF.H:player->tilex
	/// </summary>
	public int GetPlayerTileX() => simulator.PlayerTileX;

	/// <summary>
	/// Get the player's current tile Y coordinate.
	/// WL_DEF.H:player->tiley
	/// </summary>
	public int GetPlayerTileY() => simulator.PlayerTileY;

	/// <summary>
	/// Get the player's current X position (16.16 fixed-point).
	/// WL_DEF.H:player->x
	/// </summary>
	public int GetPlayerX() => simulator.PlayerX;

	/// <summary>
	/// Get the player's current Y position (16.16 fixed-point).
	/// WL_DEF.H:player->y
	/// </summary>
	public int GetPlayerY() => simulator.PlayerY;

	/// <summary>
	/// Calculate Manhattan distance to player (in tiles).
	/// </summary>
	public int CalculateDistanceToPlayer()
	{
		int dx = System.Math.Abs(GetPlayerTileX() - actor.TileX);
		int dy = System.Math.Abs(GetPlayerTileY() - actor.TileY);
		int distance = dx + dy;
		actor.DistanceToPlayer = distance;
		return distance;
	}

	/// <summary>
	/// Find nearby actors within a radius.
	/// </summary>
	/// <param name="radius">Search radius in tiles</param>
	/// <returns>Number of actors found (for now, just a count)</returns>
	public int FindNearbyActors(int radius)
	{
		// TODO: Implement spatial query for nearby actors
		// Could return actor IDs or populate a list
		return 0;
	}

	/// <summary>
	/// Emit a custom event from the actor script.
	/// Useful for sounds, visual effects, etc.
	/// </summary>
	/// <param name="eventType">Event type identifier (e.g., "bark", "alert", "attack")</param>
	public void EmitEvent(string eventType)
	{
		// TODO: Create and emit custom actor event
		// simulator.EmitCustomActorEvent(actor, eventType);
	}

	// ActionScriptContext abstract method implementations

	/// <summary>
	/// Play a digi sound by name (string-based for Lua).
	/// WL_STATE.C:PlaySoundLocActor
	/// Sound will be attached to this actor and move with it during playback.
	/// </summary>
	/// <param name="soundName">Sound name (e.g., "HALTSND")</param>
	public void PlayDigiSound(string soundName)
	{
		// Emit actor sound event - presentation layer will attach sound to actor
		simulator.EmitActorPlaySound(actorIndex, soundName);
	}

	public override void PlayDigiSound(int soundId)
	{
		// For actors, sounds should be positional (3D audio)
		// TODO: Emit event with actor position for spatial audio
		// simulator.EmitPlaySoundEvent(soundId, isPositional: true, actor.X, actor.Y);
	}

	public override void PlayMusic(int musicId)
	{
		// Music is global (not positional)
		// TODO: Emit event
	}

	public override void StopMusic()
	{
		// TODO: Emit event
	}

	public override void SpawnActor(int type, int x, int y)
	{
		// TODO: Implement actor spawning from scripts
	}

	public override void DespawnActor(int actorId)
	{
		// TODO: Implement actor despawning
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
		// TODO: Implement player healing (for friendly NPCs?)
	}

	public override void DamagePlayer(int amount)
	{
		// TODO: Get actual player reference and damage it
		// For now, just a placeholder
		// simulator.Player.TakeDamage(amount);
	}

	/// <summary>
	/// Move actor forward in its current facing direction.
	/// Based on WL_ACT2.C movement logic.
	/// </summary>
	/// <returns>True if movement succeeded, false if blocked</returns>
	public bool MoveForward()
	{
		// Calculate target position based on facing and speed
		// Speed is in fixed-point, so we need to apply it correctly
		int speed = actor.Speed;
		int dx = 0, dy = 0;

		// Convert facing direction to dx/dy delta
		switch (actor.Facing)
		{
			case Direction.E:  dx = speed; break;
			case Direction.NE: dx = speed; dy = -speed; break;
			case Direction.N:  dy = -speed; break;
			case Direction.NW: dx = -speed; dy = -speed; break;
			case Direction.W:  dx = -speed; break;
			case Direction.SW: dx = -speed; dy = speed; break;
			case Direction.S:  dy = speed; break;
			case Direction.SE: dx = speed; dy = speed; break;
		}

		// Apply movement (simplified - no collision detection yet)
		actor.X += dx;
		actor.Y += dy;

		// Update tile coordinates
		actor.TileX = (ushort)(actor.X >> 16);
		actor.TileY = (ushort)(actor.Y >> 16);

		// TODO: Implement proper collision detection
		// TODO: Emit ActorMovedEvent

		return true;
	}

	/// <summary>
	/// Turn actor to face toward the player.
	/// Based on WL_ACT1.C:SelectChaseDir logic.
	/// </summary>
	public void FacePlayer()
	{
		int dx = simulator.PlayerX - actor.X;
		int dy = simulator.PlayerY - actor.Y;

		// Calculate 8-way direction
		// Simplified angle calculation
		double angle = System.Math.Atan2(dy, dx);
		int dirInt = (int)(((angle + System.Math.PI) / (2 * System.Math.PI)) * 8 + 0.5) % 8;
		actor.Facing = (Direction)dirInt;
	}

	public override void GivePlayerAmmo(int weaponType, int amount)
	{
		// TODO: Implement ammo (for friendly NPCs giving items?)
	}

	public override void GivePlayerKey(int keyColor)
	{
		// TODO: Implement key giving
	}

	public override bool PlayerHasKey(int keyColor)
	{
		// TODO: Return actual key state
		return false;
	}
}
