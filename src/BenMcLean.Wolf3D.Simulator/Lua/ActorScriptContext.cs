using System.Linq;
using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Simulator.Entities;
using Microsoft.Extensions.Logging;
using static BenMcLean.Wolf3D.Assets.Gameplay.MapAnalyzer;

namespace BenMcLean.Wolf3D.Simulator.Lua;

/// <summary>
/// Script context for actor Think and Action functions.
/// Extends EntityScriptContext with actor-specific API for AI, movement, combat, and state transitions.
/// Inherits PlayLocalDigiSound for positional audio at actor's location.
/// </summary>
public class ActorScriptContext : EntityScriptContext
{
	private readonly Actor actor;
	private readonly int actorIndex;
	private readonly MapAnalysis mapAnalysis;

	public ActorScriptContext(
		Simulator simulator,
		Actor actor,
		int actorIndex,
		RNG rng,
		GameClock gameClock,
		MapAnalysis mapAnalysis,
		ILogger logger = null)
		: base(simulator, rng, gameClock, actor.TileX, actor.TileY, logger)
	{
		this.actor = actor;
		this.actorIndex = actorIndex;
		this.mapAnalysis = mapAnalysis;
	}
	#region Logging
	/// <summary>
	/// Log a debug message from Lua script.
	/// Routes to Microsoft.Extensions.Logging.
	/// </summary>
	public void Log(string message) => _logger?.LogDebug("Lua: {message}", message);
	#endregion Logging
	#region Actor Property Accessors
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

	/// <summary>Check if actor has a specific flag set</summary>
	public bool HasFlag(int flagValue) => (actor.Flags & (ActorFlags)flagValue) != 0;

	/// <summary>Get current distance to next tile (ob->distance)</summary>
	public int GetDistance() => actor.Distance;

	/// <summary>
	/// Check if actor has a valid direction (not nodir/null).
	/// For Lua scripts to check ob->dir != nodir.
	/// </summary>
	public bool HasDirection() => actor.Facing != null;
	#endregion Actor Property Accessors
	#region Actor Mutation Methods
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

	/// <summary>WL_STATE.C: Set actor reaction timer (ob->temp2)</summary>
	public void SetReactionTimer(int value) => actor.ReactionTimer = (short)value;

	/// <summary>Set a flag on the actor</summary>
	public void SetFlag(int flagValue) => actor.Flags |= (ActorFlags)flagValue;
	/// <summary>Clear a flag on the actor</summary>
	public void ClearFlag(int flagValue) => actor.Flags &= ~(ActorFlags)flagValue;

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
			simulator.TransitionActorStateByName(actorIndex, stateName);
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
		if (simulator.TryGetPatrolDirection(actor.TileX, actor.TileY, out Direction direction))
			// Found patrol point - change direction
			actor.Facing = direction;
		// If no patrol point found, keep current direction (don't modify actor.Facing)
		// WL_ACT2.C:4058 - Set distance to move one full tile
		actor.Distance = 0x10000; // TILEGLOBAL
		// WL_ACT2.C:4060 - TryWalk: update tilex/tiley to destination and check collision
		if (actor.Facing is not null)
		{
			// Save current position in case move is blocked
			ushort oldTileX = actor.TileX,
				oldTileY = actor.TileY;
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
	/// <summary>Set distance to next tile (ob->distance)</summary>
	public void SetDistance(int distance) => actor.Distance = distance;
	/// <summary>
	/// Turn actor to face a specific point.
	/// </summary>
	/// <param name="targetX">Target X coordinate (fixed-point)</param>
	/// <param name="targetY">Target Y coordinate (fixed-point)</param>
	public void TurnToward(int targetX, int targetY)
	{
		// Calculate direction from actor to target
		int dx = targetX - actor.X,
			dy = targetY - actor.Y;
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
		int dx = simulator.PlayerX - actor.X,
			dy = simulator.PlayerY - actor.Y;
		// Calculate 8-way direction
		// Simplified angle calculation
		double angle = System.Math.Atan2(dy, dx);
		int dirInt = (int)(((angle + System.Math.PI) / (2 * System.Math.PI)) * 8 + 0.5) % 8;
		actor.Facing = (Direction)dirInt;
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
				simulator.OpenDoor((ushort)doorIndex);
			// else: door is already Open or Opening, do nothing (just wait)
		}
	}
	#endregion Actor Mutation Methods
	#region Global Simulator Queries
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
	/// Check if a tile is navigable (walkable, no walls, no actors).
	/// WL_ACT1.C:TryWalk equivalent.
	/// </summary>
	public bool IsTileNavigable(int tileX, int tileY) =>
		simulator.IsTileNavigable((ushort)tileX, (ushort)tileY);
	/// <summary>
	/// Check if a door is fully open.
	/// WL_ACT2.C:4099 - doorobjlist[doornum].action == dr_open
	/// </summary>
	/// <param name="doorIndex">Door index (0-based)</param>
	/// <returns>True if door is fully open</returns>
	public bool IsDoorOpen(int doorIndex) =>
		doorIndex >= 0 &&
		doorIndex < simulator.Doors.Count &&
		simulator.Doors[doorIndex].Action == DoorAction.Open;
	#endregion Global Simulator Queries
	#region Line of Sight
	/// <summary>
	/// Check if actor can see the player (used for initial detection).
	/// WL_STATE.C:CheckSight (lines 1491-1545)
	/// Performs area check, close-range auto-detection, field-of-view check, then calls CheckLine.
	/// The actor must be facing toward the player's general direction (N/S/E/W quadrant).
	/// Returns true if player has been spotted.
	/// </summary>
	public bool CheckSight()
	{
		if (mapAnalysis is null)
			// Fallback if no map analysis available
			return CalculateDistanceToPlayer() < 20;
		int actorTileX = actor.TileX,
			actorTileY = actor.TileY,
			playerTileX = simulator.PlayerTileX,
			playerTileY = simulator.PlayerTileY;
		// Check field of view (90 degrees centered on facing direction)
		if (actor.Facing.HasValue)
		{
			// Calculate angle from actor to player
			// Wolf3D coordinates: +X=east, +Y=south (down map), -Y=north (up map)
			// Direction enum: N=90°=-Y, E=0°=+X, S=270°=+Y, W=180°=-X
			// Negate Y to match Direction enum's convention where North=positive angle
			float playerDx = playerTileX - actorTileX,
				playerDy = playerTileY - actorTileY,
				angleToPlayer = (float)System.Math.Atan2(-playerDy, playerDx);
			// Normalize to [0, 2π)
			if (angleToPlayer < 0) angleToPlayer += (float)(2f * System.Math.PI);
			// Get actor's facing angle
			// Direction enum: E=0, NE=1, N=2, NW=3, W=4, SW=5, S=6, SE=7
			// Convert to radians: E=0, NE=π/4, N=π/2, etc.
			float facingAngle = (byte)actor.Facing.Value * (float)(System.Math.PI / 4f),
			// Calculate angular difference
				angleDiff = angleToPlayer - facingAngle;
			// Normalize to [-PI, PI]
			while (angleDiff > System.Math.PI) angleDiff -= (float)(2f * System.Math.PI);
			while (angleDiff < -System.Math.PI) angleDiff += (float)(2f * System.Math.PI);
			// Check if within 90-degree FOV (45 degrees on each side)
			if (System.Math.Abs(angleDiff) > System.Math.PI / 4)
				return false; // Player is outside field of view
		}
		// Bresenham's line algorithm to walk from actor to player
		int dx = System.Math.Abs(playerTileX - actorTileX),
			dy = System.Math.Abs(playerTileY - actorTileY),
			sx = actorTileX < playerTileX ? 1 : -1,
			sy = actorTileY < playerTileY ? 1 : -1,
			err = dx - dy,
			x = actorTileX,
			y = actorTileY;
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
			bool movedX = false, movedY = false;
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
	/// <summary>
	/// Check if there's a clear line to player, ignoring field of view (used for shooting).
	/// WL_STATE.C:CheckLine (lines 1203-1489)
	/// Pure raycast - only checks if walls/doors block the line, no FOV or area checks.
	/// Called by CheckSight after FOV verification, and by T_Shoot to verify shot clearance.
	/// The actor can shoot even if not perfectly facing the player (chase already turned them).
	/// </summary>
	public bool CheckLine()
	{
		if (mapAnalysis is null)
		{
			// Fallback if no map analysis available
			return CalculateDistanceToPlayer() < 20;
		}
		int actorTileX = actor.TileX,
			actorTileY = actor.TileY,
			playerTileX = simulator.PlayerTileX,
			playerTileY = simulator.PlayerTileY,
			// No FOV check - CheckLine is used for shooting and doesn't care about facing direction
			// Bresenham's line algorithm to walk from actor to player
			dx = System.Math.Abs(playerTileX - actorTileX),
			dy = System.Math.Abs(playerTileY - actorTileY),
			sx = actorTileX < playerTileX ? 1 : -1,
			sy = actorTileY < playerTileY ? 1 : -1,
			err = dx - dy,
			x = actorTileX,
			y = actorTileY;
		while (true)
		{
			// Reached player position - line of sight is clear
			if (x == playerTileX && y == playerTileY)
				return true;
			// Check if current tile blocks sight
			// Skip the actor's own tile (we start there)
			if (!(x == actorTileX && y == actorTileY)
				&& !simulator.IsTileTransparentForSight((ushort)x, (ushort)y))
				// Check static transparency (walls) AND dynamic obstacles (doors, pushwalls)
					return false; // Obstacle blocks sight
			// Bresenham step - check both intermediate tiles if moving diagonally
			int e2 = 2 * err;
			bool movedX = false, movedY = false;
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
	#endregion Line of Sight
	#region Pathfinding & Navigation
	/// <summary>
	/// Try all 8 directions in random order until one is navigable.
	/// WL_STATE.C:589-612 - Fixes bug in original code that only tried 3 directions.
	/// </summary>
	/// <param name="avoidTurnaround">If true, skip the opposite of current facing direction</param>
	/// <param name="canOpenDoors">Whether this actor can open doors</param>
	/// <returns>True if a valid direction was found and set, false if all blocked</returns>
	public bool TryAllDirectionsRandom(bool avoidTurnaround = true, bool canOpenDoors = true)
	{
		// Calculate opposite direction if we should avoid turnaround
		int opposite = -1;
		if (avoidTurnaround && actor.Facing.HasValue)
			opposite = GetOppositeDirection((int)actor.Facing.Value);
		// Create array of all 8 directions
		int[] directions = [0, 1, 2, 3, 4, 5, 6, 7];
		// Fisher-Yates shuffle using the actor's RNG
		for (int i = 7; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(directions[j], directions[i]) = (directions[i], directions[j]);
		}
		// Try each direction
		foreach (int dir in directions)
		{
			if (avoidTurnaround && dir == opposite)
				continue;
			if (TryDirection(dir, canOpenDoors))
				return true;
		}
		// All directions blocked
		actor.Facing = null;
		return false;
	}
	/// <summary>
	/// Try to set facing to a specific direction and check if it's navigable.
	/// WL_STATE.C:TryWalk - Updates tilex/tiley to destination and checks navigability.
	/// </summary>
	/// <param name="dir">Direction to try (0-7, or -1 for nodir)</param>
	/// <param name="canOpenDoors">Whether this actor can open doors (false for dogs)</param>
	/// <returns>True if direction is valid and navigable, false otherwise</returns>
	public bool TryDirection(int dir, bool canOpenDoors = true)
	{
		if (dir < 0 || dir > 7)
			return false;
		actor.Facing = (Direction)dir;
		// Save current position in case move is blocked
		ushort oldTileX = actor.TileX,
			oldTileY = actor.TileY;
		// WL_STATE.C:217-354 - Update tilex/tiley to DESTINATION tile
		// For diagonal movements, also check adjacent tiles (WL_STATE.C:274-355)
		bool isDiagonal = false;
		ushort checkX1 = 0, checkY1 = 0, checkX2 = 0, checkY2 = 0;
		switch ((Direction)dir)
		{
			case Direction.E:  actor.TileX++; break;
			case Direction.NE:
				actor.TileX++; actor.TileY--;
				isDiagonal = true;
				checkX1 = actor.TileX; checkY1 = oldTileY;  // East
				checkX2 = oldTileX; checkY2 = actor.TileY;  // North
				break;
			case Direction.N:  actor.TileY--; break;
			case Direction.NW:
				actor.TileX--; actor.TileY--;
				isDiagonal = true;
				checkX1 = actor.TileX; checkY1 = oldTileY;  // West
				checkX2 = oldTileX; checkY2 = actor.TileY;  // North
				break;
			case Direction.W:  actor.TileX--; break;
			case Direction.SW:
				actor.TileX--; actor.TileY++;
				isDiagonal = true;
				checkX1 = actor.TileX; checkY1 = oldTileY;  // West
				checkX2 = oldTileX; checkY2 = actor.TileY;  // South
				break;
			case Direction.S:  actor.TileY++; break;
			case Direction.SE:
				actor.TileX++; actor.TileY++;
				isDiagonal = true;
				checkX1 = actor.TileX; checkY1 = oldTileY;  // East
				checkX2 = oldTileX; checkY2 = actor.TileY;  // South
				break;
		}
		// WL_STATE.C:274-355 - CHECKDIAG for diagonal movements
		// Check adjacent tiles to prevent corner-cutting
		if (isDiagonal)
		{
			if (!simulator.IsTileNavigable(checkX1, checkY1) ||
			    !simulator.IsTileNavigable(checkX2, checkY2))
			{
				// Blocked by adjacent tile
				actor.TileX = oldTileX;
				actor.TileY = oldTileY;
				return false;
			}
		}
		// WL_STATE.C:260-346 - Dogs use CHECKDIAG (no doors), others use CHECKSIDE (handles doors)
		if (!isDiagonal && canOpenDoors)
		{
			// WL_STATE.C:364-369 - Check for doors (only for actors that can open them on cardinal movements)
			int doorIndex = simulator.GetDoorIndexAtTile(actor.TileX, actor.TileY);
			if (doorIndex >= 0)
			{
				// Found a door - request it to open
				simulator.OpenDoor((ushort)doorIndex);
				// WL_STATE.C:367 - Set distance to -(doornum+1) to indicate waiting for door
				actor.Distance = -(doorIndex + 1);
				return true;
			}
		}
		// WL_STATE.C:372-375 - Check if navigable
		if (simulator.IsTileNavigable(actor.TileX, actor.TileY))
		{
			// WL_STATE.C:375 - TryWalk sets ob->distance = TILEGLOBAL
			actor.Distance = 0x10000; // TILEGLOBAL - one full tile
			return true;
		}
		// Blocked - revert tile position
		actor.TileX = oldTileX;
		actor.TileY = oldTileY;
		return false;
	}
	/// <summary>
	/// Select direction to chase player using cardinal directions only.
	/// WL_STATE.C:SelectChaseDir (lines 528-615) - Direct pursuit pathfinding.
	/// Tries preferred cardinals, then current direction, then random fallback.
	/// </summary>
	/// <param name="canOpenDoors">Whether this actor can open doors</param>
	/// <returns>True if a valid direction was found and set, false if blocked</returns>
	public bool SelectChaseDir(bool canOpenDoors = true)
	{
		// Calculate delta to player
		int dx = simulator.PlayerTileX - actor.TileX,
			dy = simulator.PlayerTileY - actor.TileY;
		// Get preferred cardinal directions
		(int d1, int d2) = GetCardinalDirections(dx, dy, prioritizeLarger: true);
		// Skip turnaround direction
		int opposite = actor.Facing.HasValue ? GetOppositeDirection((int)actor.Facing.Value) : -1;
		if (d1 == opposite) d1 = -1;
		if (d2 == opposite) d2 = -1;
		// Try primary direction (WL_STATE.C:566)
		if (TryDirection(d1, canOpenDoors))
			return true;
		// Try secondary direction (WL_STATE.C:573)
		if (TryDirection(d2, canOpenDoors))
			return true;
		// Try keeping current direction - momentum (WL_STATE.C:582)
		if (actor.Facing.HasValue && TryDirection((int)actor.Facing.Value, canOpenDoors))
			return true;
		// Random search through all 8 directions (WL_STATE.C:589)
		// Fixes original bug that only tried 3 directions
		if (TryAllDirectionsRandom(avoidTurnaround: true, canOpenDoors))
			return true;
		// All directions blocked
		actor.Facing = null;
		return false;
	}
	/// <summary>
	/// Select direction to dodge - evasive movement toward player with diagonal preference.
	/// WL_STATE.C:SelectDodgeDir (lines 404-515) - Erratic pursuit pathfinding.
	/// Tries diagonal + randomized cardinals, then turnaround as last resort.
	/// </summary>
	/// <param name="canOpenDoors">Whether this actor can open doors</param>
	/// <returns>True if a valid direction was found and set, false if blocked</returns>
	public bool SelectDodgeDir(bool canOpenDoors = true)
	{
		// Calculate delta to player
		int dx = simulator.PlayerTileX - actor.TileX,
			dy = simulator.PlayerTileY - actor.TileY;
		// Get 5 dodge directions: diagonal + 4 randomized cardinals
		int[] dodgeDirs = GetDodgeDirections(dx, dy);
		// Get turnaround direction to avoid (WL_STATE.C:411)
		// Note: Original has FL_FIRSTATTACK logic here, simplified for now
		int turnaround = actor.Facing.HasValue ? GetOppositeDirection((int)actor.Facing.Value) : -1;
		// Try each direction in priority order (WL_STATE.C:493)
		for (int i = 0; i < 5; i++)
		{
			int dir = dodgeDirs[i];
			if (dir >= 0 && dir != turnaround)
			{
				if (TryDirection(dir, canOpenDoors))
					return true;
			}
		}
		// Turn around only as last resort (WL_STATE.C:506)
		if (turnaround >= 0 && TryDirection(turnaround, canOpenDoors))
			return true;
		// All directions blocked
		actor.Facing = null;
		return false;
	}
	#endregion Pathfinding & Navigation
	#region Direction Utilities
	/// <summary>
	/// Get the opposite direction (for turnaround avoidance).
	/// WL_STATE.C:opposite[] array
	/// </summary>
	public int GetOppositeDirection(int dir)
	{
		if (dir < 0 || dir > 7) return -1;
		Direction direction = (Direction)dir;
		Direction opposite = direction switch
		{
			Direction.E  => Direction.W,
			Direction.NE => Direction.SW,
			Direction.N  => Direction.S,
			Direction.NW => Direction.SE,
			Direction.W  => Direction.E,
			Direction.SW => Direction.NE,
			Direction.S  => Direction.N,
			Direction.SE => Direction.NW,
			_ => Direction.E
		};
		return (int)opposite;
	}
	private Direction GetOppositeDirection(Direction dir) => (Direction)GetOppositeDirection((int)dir);
	/// <summary>
	/// Get diagonal direction from two cardinal directions.
	/// WL_STATE.C:diagonal[][] lookup table
	/// </summary>
	/// <param name="dir1">First cardinal direction (E/N/W/S only)</param>
	/// <param name="dir2">Second cardinal direction (E/N/W/S only)</param>
	/// <returns>Diagonal direction, or -1 if invalid combination</returns>
	public int GetDiagonalDirection(int dir1, int dir2)
	{
		// WL_STATE.C:27-38 diagonal[9][9] lookup table
		// Only cardinal directions produce valid diagonals
		if (dir1 == 0 && dir2 == 2) return 1;  // E + N = NE
		if (dir1 == 2 && dir2 == 0) return 1;  // N + E = NE
		if (dir1 == 2 && dir2 == 4) return 3;  // N + W = NW
		if (dir1 == 4 && dir2 == 2) return 3;  // W + N = NW
		if (dir1 == 4 && dir2 == 6) return 5;  // W + S = SW
		if (dir1 == 6 && dir2 == 4) return 5;  // S + W = SW
		if (dir1 == 6 && dir2 == 0) return 7;  // S + E = SE
		if (dir1 == 0 && dir2 == 6) return 7;  // E + S = SE
		return -1; // Invalid combination
	}
	/// <summary>
	/// Get primary and secondary cardinal directions toward a target.
	/// WL_STATE.C:SelectChaseDir - direction selection logic
	/// </summary>
	/// <param name="deltaX">X distance to target (target.x - actor.x)</param>
	/// <param name="deltaY">Y distance to target (target.y - actor.y)</param>
	/// <param name="prioritizeLarger">If true, swap d1/d2 if Y distance > X distance</param>
	/// <returns>Tuple of (primary direction, secondary direction). Either can be -1 if delta is 0.</returns>
	public (int d1, int d2) GetCardinalDirections(int deltaX, int deltaY, bool prioritizeLarger = true)
	{
		int d1 = -1, d2 = -1;
		// Select cardinal direction based on X delta
		if (deltaX > 0)
			d1 = 0;  // east
		else if (deltaX < 0)
			d1 = 4;  // west
		// Select cardinal direction based on Y delta
		if (deltaY > 0)
			d2 = 6;  // south
		else if (deltaY < 0)
			d2 = 2;  // north
		// Prioritize direction with larger delta (WL_STATE.C:553)
		if (prioritizeLarger)
		{
			int absDx = deltaX < 0 ? -deltaX : deltaX,
				absDy = deltaY < 0 ? -deltaY : deltaY;

			if (absDy > absDx)
				// Swap so primary direction is the one with larger delta
				(d2, d1) = (d1, d2);
		}
		return (d1, d2);
	}
	/// <summary>
	/// Get 5 dodge directions (diagonal + 4 randomized cardinals) toward target.
	/// WL_STATE.C:SelectDodgeDir - direction array setup.
	/// Useful for modders who want the dodge direction logic but custom behavior.
	/// </summary>
	/// <param name="deltaX">X distance to target</param>
	/// <param name="deltaY">Y distance to target</param>
	/// <returns>Array of 5 directions: [0]=diagonal, [1-4]=randomized cardinals</returns>
	public int[] GetDodgeDirections(int deltaX, int deltaY)
	{
		int[] dirtry = [-1, -1, -1, -1, -1];
		// WL_STATE.C:432 - Select cardinals based on delta to player
		if (deltaX > 0)
		{
			dirtry[1] = 0;  // east
			dirtry[3] = 4;  // west
		}
		else
		{
			dirtry[1] = 4;  // west
			dirtry[3] = 0;  // east
		}
		if (deltaY > 0)
		{
			dirtry[2] = 6;  // south
			dirtry[4] = 2;  // north
		}
		else
		{
			dirtry[2] = 2;  // north
			dirtry[4] = 6;  // south
		}
		// WL_STATE.C:465 - Randomize based on which delta is larger
		int absDx = deltaX < 0 ? -deltaX : deltaX,
			absDy = deltaY < 0 ? -deltaY : deltaY;
		if (absDx > absDy)
		{
			// Swap priorities
			(dirtry[2], dirtry[1]) = (dirtry[1], dirtry[2]);
			(dirtry[4], dirtry[3]) = (dirtry[3], dirtry[4]);
		}
		// WL_STATE.C:478 - Additional randomization
		if (rng.Next(256) < 128)
		{
			(dirtry[2], dirtry[1]) = (dirtry[1], dirtry[2]);
			(dirtry[4], dirtry[3]) = (dirtry[3], dirtry[4]);
		}
		// WL_STATE.C:488 - Compute diagonal from first two cardinals
		dirtry[0] = GetDiagonalDirection(dirtry[1], dirtry[2]);
		if (dirtry[0] == -1)
			dirtry[0] = dirtry[1]; // Fallback to first cardinal
		return dirtry;
	}
	#endregion Direction Utilities
	#region Utilities
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
	#endregion Utilities
	#region ActionScriptContext Abstract Method Implementations
	/// <summary>
	/// Play a positional digi sound at this actor's location.
	/// WL_STATE.C:PlaySoundLocActor
	/// Sound will be attached to this actor and move with it during playback.
	/// Overrides EntityScriptContext.PlayLocalDigiSound to use actor-specific sound emission.
	/// </summary>
	/// <param name="soundName">Sound name (e.g., "HALTSND")</param>
	public override void PlayLocalDigiSound(string soundName) => simulator.EmitActorPlaySound(actorIndex, soundName);

	// PlayDigiSound (global), PlayAdLibSound, PlayMusic, and StopMusic are inherited from BaseScriptContext
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
	#endregion ActionScriptContext Abstract Method Implementations
}
