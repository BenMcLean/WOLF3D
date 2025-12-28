using System.Collections.Generic;
using System.Linq;
using BenMcLean.Wolf3D.Assets;
using Microsoft.Extensions.Logging;
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
	MapAnalysis mapAnalysis,
	Microsoft.Extensions.Logging.ILogger logger) : ActionScriptContext(simulator, rng, gameClock)
{
	private readonly Actor actor = actor;
	private readonly int actorIndex = actorIndex;
	private readonly MapAnalysis mapAnalysis = mapAnalysis;
	private readonly Microsoft.Extensions.Logging.ILogger logger = logger;

	// Actor property accessors (read-only from Lua)

	/// <summary>
	/// Get actor type identifier (e.g., "guard", "ss", "dog", "mutant").
	/// Lua scripts can use this to implement type-specific behavior,
	/// similar to how original Wolf3D used switch(ob->obclass).
	/// </summary>
	public string GetActorType() => actor.ActorType;
	/// <summary>Get actor's current tile X coordinate</summary>
	public int GetTileX() => actor.TileX;

	/// <summary>Get actor's current tile Y coordinate</summary>
	public int GetTileY() => actor.TileY;

	/// <summary>Get actor's fixed-point X coordinate</summary>
	public int GetX() => actor.X;

	/// <summary>Get actor's fixed-point Y coordinate</summary>
	public int GetY() => actor.Y;

	/// <summary>
	/// Set actor's exact position (fixed-point coordinates).
	/// Used for snapping to tile centers after crossing boundaries.
	/// NOTE: Does NOT update spatial index - caller (MoveObj) already did that.
	/// </summary>
	public void SetPosition(int x, int y)
	{
		actor.X = x;
		actor.Y = y;
		// Update tile coordinates (spatial index already updated by MoveObj)
		actor.TileX = (ushort)(x >> 16);
		actor.TileY = (ushort)(y >> 16);
	}
	/// <summary>Get actor's current hit points</summary>
	public int GetHitPoints() => actor.HitPoints;

	/// <summary>Get actor's current facing direction (0-7)</summary>
	public int GetFacing() => (int)actor.Facing;

	/// <summary>Get actor's current speed</summary>
	public int GetSpeed() => actor.Speed;

	/// <summary>
	/// Calculate distance to player using tile coordinates.
	/// Original Wolf3D calculates this on-demand in AI functions, not stored.
	/// </summary>
	public int CalculateDistanceToPlayer()
	{
		int dx = System.Math.Abs(simulator.PlayerTileX - actor.TileX);
		int dy = System.Math.Abs(simulator.PlayerTileY - actor.TileY);
		return System.Math.Max(dx, dy); // Chebyshev distance (matching original)
	}

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
	/// WL_DEF.H:US_RndT() - Returns a random byte value using the table method.
	/// Used throughout Wolf3D for reaction times, AI decisions, etc.
	/// </summary>
	public int US_RndT() => rng.Next(256);

	/// <summary>
	/// Bitwise right shift for Lua (value >> bits).
	/// Lua doesn't have native bit shift operators, so we provide them here.
	/// </summary>
	public int BitShiftRight(int value, int bits) => value >> bits;
	/// <summary>
	/// Bitwise left shift for Lua (value << bits).
	/// Lua doesn't have native bit shift operators, so we provide them here.
	/// </summary>
	public int BitShiftLeft(int value, int bits) => value << bits;
	/// <summary>
	/// Check line of sight to player using raycasting.
	/// WL_STATE.C:CheckSight(ob) and CheckLine(ob)
	/// Walks from actor to player tile-by-tile, checking for walls, closed doors, and pushwalls.
	/// Actors with a facing direction only see within 90 degrees centered on their facing.
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

		// Check field of view (90 degrees centered on facing direction)
		if (actor.Facing.HasValue)
		{
			// Calculate angle from actor to player
			// Wolf3D coordinates: +X=east, +Y=south (down map), -Y=north (up map)
			// Direction enum: N=90°=-Y, E=0°=+X, S=270°=+Y, W=180°=-X
			// Negate Y to match Direction enum's convention where North=positive angle
			float playerDx = playerTileX - actorTileX;
			float playerDy = playerTileY - actorTileY;
			float angleToPlayer = (float)System.Math.Atan2(-playerDy, playerDx);
			// Normalize to [0, 2π)
			if (angleToPlayer < 0) angleToPlayer += (float)(2 * System.Math.PI);
			// Get actor's facing angle
			// Direction enum: E=0, NE=1, N=2, NW=3, W=4, SW=5, S=6, SE=7
			// Convert to radians: E=0, NE=π/4, N=π/2, etc.
			float facingAngle = (byte)actor.Facing.Value * (float)(System.Math.PI / 4);
			// Calculate angular difference
			float angleDiff = angleToPlayer - facingAngle;
			// Normalize to [-PI, PI]
			while (angleDiff > System.Math.PI) angleDiff -= (float)(2 * System.Math.PI);
			while (angleDiff < -System.Math.PI) angleDiff += (float)(2 * System.Math.PI);
			// Check if within 90-degree FOV (45 degrees on each side)
			if (System.Math.Abs(angleDiff) > System.Math.PI / 4)
				return false; // Player is outside field of view
		}
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
				// Check static transparency (walls) AND dynamic obstacles (doors, pushwalls)
				if (!simulator.IsTileTransparentForSight((ushort)x, (ushort)y))
					return false; // Obstacle blocks sight
			}

			// Bresenham step - check both intermediate tiles if moving diagonally
			int e2 = 2 * err;
			bool movedX = false;
			bool movedY = false;
			if (e2 > -dy)
			{
				err -= dy;
				x += sx;
				movedX = true;
			}
			if (e2 < dx)
			{
				err += dx;
				y += sy;
				movedY = true;
			}
			// If we moved diagonally, check the intermediate tile we might have cut through
			// This prevents seeing through corners
			if (movedX && movedY)
			{
				// Check the tile at (x - sx, y) - the tile we passed through horizontally
				if (!simulator.IsTileTransparentForSight((ushort)(x - sx), (ushort)y))
					return false;
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
	/// WL_ACT2.C:SelectPathDir (lines 4046-4062)
	/// Reads patrol direction from current tile (optional turn marker).
	/// If patrol point found, sets ob->dir to new direction.
	/// Sets ob->distance = TILEGLOBAL and calls TryWalk.
	/// If TryWalk fails (blocked), sets ob->dir = nodir (null in C#).
	/// NOTE: This updates actor.TileX/TileY to DESTINATION tile (ob->tilex/tiley).
	/// </summary>
	public void SelectPathDir()
	{
		// WL_ACT2.C:4050 - Read patrol arrow at current tile (optional turn marker)
		if (simulator.TryGetPatrolDirection(actor.TileX, actor.TileY, out Assets.Direction direction))
		{
			// Found patrol point - change direction
			actor.Facing = direction;
		}
		// If no patrol point found, keep current direction (don't modify actor.Facing)
		// WL_ACT2.C:4058 - Set distance to move one full tile
		actor.Distance = 0x10000; // TILEGLOBAL
		// WL_ACT2.C:4060 - TryWalk: update tilex/tiley to destination and check collision
		if (actor.Facing != null)
		{
			// Save current position in case move is blocked
			ushort oldTileX = actor.TileX;
			ushort oldTileY = actor.TileY;
			// WL_STATE.C:216-250 - Update tilex/tiley based on direction
			switch (actor.Facing.Value)
			{
				case Direction.N:  actor.TileY--; break;
				case Direction.NE: actor.TileX++; actor.TileY--; break;
				case Direction.E:  actor.TileX++; break;
				case Direction.SE: actor.TileX++; actor.TileY++; break;
				case Direction.S:  actor.TileY++; break;
				case Direction.SW: actor.TileX--; actor.TileY++; break;
				case Direction.W:  actor.TileX--; break;
				case Direction.NW: actor.TileX--; actor.TileY--; break;
			}
			// WL_STATE.C:253+ - Check if destination tile is navigable
			// WL_STATE.C:364-369 - If blocked by door, open it and set distance = -doornum-1
			int doorIndex = simulator.GetDoorIndexAtTile(actor.TileX, actor.TileY);
			if (doorIndex >= 0)
			{
				// Found a door - request it to open
				simulator.OpenDoor((ushort)doorIndex);
				// WL_STATE.C:367 - Set distance to -(doornum+1) to indicate waiting for door
				actor.Distance = -(doorIndex + 1);
				// Keep the destination tile coordinates and direction
				return;
			}
			if (!simulator.IsTileNavigable(actor.TileX, actor.TileY))
			{
				// WL_ACT2.C:4061 - Blocked by wall/actor, revert position and set dir = nodir
				actor.TileX = oldTileX;
				actor.TileY = oldTileY;
				actor.Facing = null;
			}
		}
	}
	/// <summary>
	/// WL_STATE.C:MoveObj (lines 723-833)
	/// Move actor in current direction by specified distance.
	/// Updates ONLY fixed-point position (ob->x, ob->y), NOT tile coordinates.
	/// Tile coordinates (ob->tilex, ob->tiley) are ONLY updated by TryWalk/SelectPathDir.
	/// </summary>
	public void MoveObj(int move)
	{
		// Can't move if no direction (nodir)
		if (actor.Facing == null)
			return;
		// Debug: log first few moves
		if (actorIndex < 3 && move > 0)
			System.Diagnostics.Debug.WriteLine($"[MoveObj] Actor {actorIndex}: Facing={actor.Facing} (value={(int)actor.Facing}), move={move}");
		// WL_STATE.C:727-763 - Apply movement to fixed-point coordinates
		switch (actor.Facing.Value)
		{
			case Direction.N:  actor.Y -= move; break;
			case Direction.NE: actor.Y -= move; actor.X += move; break;
			case Direction.E:  actor.X += move; break;
			case Direction.SE: actor.X += move; actor.Y += move; break;
			case Direction.S:  actor.Y += move; break;
			case Direction.SW: actor.X -= move; actor.Y += move; break;
			case Direction.W:  actor.X -= move; break;
			case Direction.NW: actor.X -= move; actor.Y -= move; break;
		}
		// Decrement distance remaining to destination
		actor.Distance -= move;
		// Fire movement event
		simulator.FireActorMovedEvent(actorIndex, actor.X, actor.Y, actor.Facing);
	}
	/// <summary>Get current distance to next tile (ob->distance)</summary>
	public int GetDistance() => actor.Distance;
	/// <summary>Set distance to next tile (ob->distance)</summary>
	public void SetDistance(int distance) => actor.Distance = distance;
	/// <summary>
	/// Check if a tile is navigable (walkable, no walls, no actors).
	/// WL_ACT1.C:TryWalk equivalent.
	/// </summary>
	public bool IsTileNavigable(int tileX, int tileY) =>
		simulator.IsTileNavigable((ushort)tileX, (ushort)tileY);
	/// <summary>
	/// Check if actor has a valid direction (not nodir/null).
	/// For Lua scripts to check ob->dir != nodir.
	/// </summary>
	public bool HasDirection() => actor.Facing != null;
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

	/// <summary>
	/// Request a door to open (actor-safe version).
	/// Actors can only request opening, not close/toggle doors.
	/// Guard check prevents actors from interrupting door opening sound.
	/// WL_ACT1.C:OpenDoor
	/// </summary>
	/// <param name="doorIndex">Door index (0-based)</param>
	public void OpenDoor(int doorIndex)
	{
		if (doorIndex >= 0 && doorIndex < simulator.Doors.Count)
		{
			// Actor guard: only call OpenDoor if door is NOT already Open or Opening
			// This prevents actors from re-triggering the door opening sound
			Door door = simulator.Doors[doorIndex];
			if (door.Action != DoorAction.Open && door.Action != DoorAction.Opening)
			{
				simulator.OpenDoor((ushort)doorIndex);
			}
			// else: door is already Open or Opening, do nothing (just wait)
		}
	}

	/// <summary>
	/// Check if a door is fully open.
	/// WL_ACT2.C:4099 - doorobjlist[doornum].action == dr_open
	/// </summary>
	/// <param name="doorIndex">Door index (0-based)</param>
	/// <returns>True if door is fully open</returns>
	public bool IsDoorOpen(int doorIndex)
	{
		if (doorIndex >= 0 && doorIndex < simulator.Doors.Count)
			return simulator.Doors[doorIndex].Action == DoorAction.Open;
		return false;
	}

	// ActionScriptContext abstract method implementations

	/// <summary>
	/// Play a digi sound by name.
	/// WL_STATE.C:PlaySoundLocActor
	/// Sound will be attached to this actor and move with it during playback.
	/// </summary>
	/// <param name="soundName">Sound name (e.g., "HALTSND")</param>
	public override void PlayDigiSound(string soundName)
	{
		// Emit actor sound event - presentation layer will attach sound to actor
		simulator.EmitActorPlaySound(actorIndex, soundName);
	}

	public override void PlayMusic(string musicName)
	{
		// Music is global (not positional)
		// TODO: Emit event by music name
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
