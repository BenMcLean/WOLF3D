using BenMcLean.Wolf3D.Assets.Gameplay;
using BenMcLean.Wolf3D.Shared;
using Godot;
using System;
using System.Collections.Generic;

namespace BenMcLean.Wolf3D.VR.ActionStage;

/// <summary>
/// Manages dynamic rendering of actor sprites in a Wolfenstein 3D map.
/// Each actor is rendered as an individual MeshInstance3D node (NOT MultiMesh).
/// Actors change sprites VERY frequently (every animation frame, rotation, state change),
/// so individual nodes allow efficient material swaps without MultiMesh overhead.
/// Sprites rotate each frame to face opposite of the player's Y-axis orientation (billboard effect).
/// This node contains all actor nodes as children - just add it to the scene tree.
/// </summary>
public partial class Actors : Node3D
{
	private const float SnoozeOverlayDepthOffset = 0.001f;
	// Maps ActorIndex -> MeshInstance3D node
	private readonly Dictionary<int, MeshInstance3D> _actorNodes = [];
	// Optional Noah-style snooze overlay billboards, one per sleeping actor.
	private readonly Dictionary<int, MeshInstance3D> _snoozeNodes = [];
	// Maps ActorIndex -> AudioStreamPlayer3D for spatial sound
	private readonly Dictionary<int, AudioStreamPlayer3D> _actorSpeakers = [];
	// Maps ActorIndex -> actor rendering state
	private readonly Dictionary<int, ActorRenderData> _actorData = [];
	// Sprite materials from VRAssetManager
	private readonly IReadOnlyDictionary<ushort, StandardMaterial3D> _spriteMaterials;
	// Digi sound library from SharedAssetManager
	private readonly IReadOnlyDictionary<string, AudioStreamWav> _digiSounds;
	// Viewer position for directional sprite calculation (normally player, could be MR camera)
	private readonly Func<Vector3> _getViewerPosition;
	// Camera Y rotation delegate for billboard effect
	private readonly Func<float> _getCameraYRotation;
	// Absolute VSWAP pages for SPR_SNOOZE_1..3, if the current game defines them.
	private readonly ushort[] _snoozePages;
	// Simulator reference for event subscription
	private Simulator.Simulator _simulator;
	/// <summary>
	/// Actor rendering state (position, sprite info, facing).
	/// </summary>
	private struct ActorRenderData
	{
		public Vector3 Position;                 // World position (Godot coordinates)
		public Direction? Facing;       // Actor's facing direction (for rotated sprites, 8-way)
		public ushort BaseShape;                 // Base sprite page
		public bool IsRotated;                   // True = 8-directional, False = single sprite
		public bool IsSleeping;                  // True while Noah-style snooze overlay should animate
		public byte SnoozeCounter;               // Original Noah uses a wrapping 2.6 fixed-point snore counter
	}
	/// <summary>
	/// Creates actor sprite rendering system.
	/// Actors are spawned dynamically via ActorSpawnedEvent - no initial actors shown.
	/// </summary>
	/// <param name="spriteMaterials">Dictionary of sprite materials from VRAssetManager.SpriteMaterials</param>
	/// <param name="digiSounds">Dictionary of digi sounds from SharedAssetManager</param>
	/// <param name="getViewerPosition">Delegate that returns viewer position for directional sprites</param>
	/// <param name="getCameraYRotation">Delegate that returns camera's Y rotation in radians</param>
	public Actors(
		IReadOnlyDictionary<ushort, StandardMaterial3D> spriteMaterials,
		IReadOnlyDictionary<string, AudioStreamWav> digiSounds,
		Func<Vector3> getViewerPosition,
		Func<float> getCameraYRotation,
		IReadOnlyDictionary<string, ushort> spritePagesByName = null)
	{
		_spriteMaterials = spriteMaterials ?? throw new ArgumentNullException(nameof(spriteMaterials));
		_digiSounds = digiSounds ?? throw new ArgumentNullException(nameof(digiSounds));
		_getViewerPosition = getViewerPosition ?? throw new ArgumentNullException(nameof(getViewerPosition));
		_getCameraYRotation = getCameraYRotation ?? throw new ArgumentNullException(nameof(getCameraYRotation));
		if (spritePagesByName is not null
			&& spritePagesByName.TryGetValue("SPR_SNOOZE_1", out ushort snooze1)
			&& spritePagesByName.TryGetValue("SPR_SNOOZE_2", out ushort snooze2)
			&& spritePagesByName.TryGetValue("SPR_SNOOZE_3", out ushort snooze3))
			_snoozePages = [snooze1, snooze2, snooze3];
	}
	/// <summary>
	/// Shows a newly spawned actor.
	/// Called from event handler when ActorSpawnedEvent fires.
	/// </summary>
	private void ShowActor(int actorIndex, ushort shape, bool isRotated, bool isSleeping, byte snoozeCounter, ushort tileX, ushort tileY, Direction? facing)
	{
		// If actor already has a node (e.g., re-emitting after LoadState), remove old one
		if (_actorNodes.TryGetValue(actorIndex, out MeshInstance3D existingNode))
		{
			RemoveChild(existingNode);
			existingNode.QueueFree();
			_actorNodes.Remove(actorIndex);
			_actorSpeakers.Remove(actorIndex);
			_actorData.Remove(actorIndex);
		}
		if (_snoozeNodes.TryGetValue(actorIndex, out MeshInstance3D existingSnoozeNode))
		{
			RemoveChild(existingSnoozeNode);
			existingSnoozeNode.QueueFree();
			_snoozeNodes.Remove(actorIndex);
		}
		// Calculate world position at tile center
		Vector3 position = new(
			tileX.ToMetersCentered(),
			Constants.HalfTileHeight,
			tileY.ToMetersCentered()
		);
		// Create MeshInstance3D for this actor
		MeshInstance3D node = new()
		{
			Mesh = Constants.WallMesh,  // Shared quad mesh
			Name = $"Actor_{actorIndex}",
			Position = position,
			Rotation = new Vector3(0, _getCameraYRotation(), 0)  // Initial billboard rotation
		};
		// Set material based on sprite type
		if (isRotated)
		{
			// Calculate initial directional sprite
			ushort directionalSprite = CalculateDirectionalSprite(position, shape, _getViewerPosition(), facing);
			if (!_spriteMaterials.TryGetValue(directionalSprite, out StandardMaterial3D material))
			{
				GD.PrintErr($"ERROR: Sprite material {directionalSprite} not found!");
				return;
			}
			node.MaterialOverride = material;
		}
		else
		{
			// Single sprite for all viewing angles
			if (!_spriteMaterials.TryGetValue(shape, out StandardMaterial3D material))
			{
				GD.PrintErr($"ERROR: Sprite material {shape} not found!");
				return;
			}
			node.MaterialOverride = material;
		}
		// Create AudioStreamPlayer3D for spatial sound attached to actor
		AudioStreamPlayer3D speaker = new()
		{
			Name = $"ActorSpeaker_{actorIndex}",
			Position = Vector3.Zero, // Relative to parent node
			Bus = "DigiSounds",
		};
		node.AddChild(speaker);
		// Add to scene and track
		AddChild(node);
		_actorNodes[actorIndex] = node;
		_actorSpeakers[actorIndex] = speaker;
		if (_snoozePages is not null)
		{
			MeshInstance3D snoozeNode = new()
			{
				Mesh = Constants.WallMesh,
				Name = $"ActorSnooze_{actorIndex}",
				Position = position,
				Rotation = new Vector3(0, _getCameraYRotation(), 0),
				Visible = false
			};
			AddChild(snoozeNode);
			_snoozeNodes[actorIndex] = snoozeNode;
		}
		_actorData[actorIndex] = new ActorRenderData
		{
			Position = position,
			Facing = facing,
			BaseShape = shape,
			IsRotated = isRotated,
			IsSleeping = isSleeping,
			SnoozeCounter = snoozeCounter,
		};
		UpdateSnoozeNode(actorIndex, _actorData[actorIndex], _getViewerPosition());
	}
	/// <summary>
	/// Moves an actor to a new position.
	/// Called from event handler when ActorMovedEvent fires.
	/// </summary>
	private void MoveActor(int actorIndex, int fixedX, int fixedY)
	{
		if (!_actorNodes.TryGetValue(actorIndex, out MeshInstance3D node))
			return;
		// Convert 16.16 fixed-point to Godot world coordinates
		Vector3 newPosition = new(
			x: fixedX.ToMeters(),
			y: Constants.HalfTileHeight,
			z: fixedY.ToMeters());
		// Update node position
		node.Position = newPosition;
		// Update stored position in actor data for directional sprite calculation
		if (_actorData.TryGetValue(actorIndex, out ActorRenderData data))
		{
			data.Position = newPosition;
			_actorData[actorIndex] = data;
			UpdateSnoozeNode(actorIndex, data, _getViewerPosition());
		}
	}
	/// <summary>
	/// Changes an actor's sprite (animation, state change, rotation).
	/// Called from event handler when ActorSpriteChangedEvent fires.
	/// This fires VERY frequently - optimized for performance.
	/// </summary>
	private void ChangeActorSprite(int actorIndex, ushort newShape, bool isRotated, bool isSleeping, byte snoozeCounter)
	{
		if (!_actorNodes.TryGetValue(actorIndex, out MeshInstance3D node))
			return;
		if (!_actorData.TryGetValue(actorIndex, out ActorRenderData data))
			return;
		// Update actor data with new sprite info
		data.BaseShape = newShape;
		data.IsRotated = isRotated;
		data.IsSleeping = isSleeping;
		data.SnoozeCounter = isSleeping ? snoozeCounter : (byte)0;
		_actorData[actorIndex] = data;
		// Update material
		if (!isRotated)
		{
			// Non-rotated: just set the material directly
			if (_spriteMaterials.TryGetValue(newShape, out StandardMaterial3D material))
				node.MaterialOverride = material;
		}
		else
		{
			// Rotated: calculate directional sprite (will be updated in _Process too)
			ushort directionalSprite = CalculateDirectionalSprite(
				data.Position, newShape, _getViewerPosition(), data.Facing);
			if (_spriteMaterials.TryGetValue(directionalSprite, out StandardMaterial3D material))
				node.MaterialOverride = material;
		}
		UpdateSnoozeNode(actorIndex, data, _getViewerPosition());
	}
	/// <summary>
	/// Hides an actor (death, despawn).
	/// Called from event handler when ActorDespawnedEvent fires.
	/// </summary>
	private void HideActor(int actorIndex)
	{
		if (_actorNodes.TryGetValue(actorIndex, out MeshInstance3D node))
		{
			node.QueueFree();  // Remove from scene tree (will also free child speaker)
			_actorNodes.Remove(actorIndex);
		}
		if (_snoozeNodes.TryGetValue(actorIndex, out MeshInstance3D snoozeNode))
		{
			snoozeNode.QueueFree();
			_snoozeNodes.Remove(actorIndex);
		}
		_actorSpeakers.Remove(actorIndex);  // Remove speaker reference
		_actorData.Remove(actorIndex);
	}

	private void UpdateSnoozeNode(int actorIndex, ActorRenderData data, Vector3 viewerPosition)
	{
		if (_snoozePages is null || !_snoozeNodes.TryGetValue(actorIndex, out MeshInstance3D snoozeNode))
			return;

		if (!data.IsSleeping)
		{
			snoozeNode.Visible = false;
			return;
		}

		int snoozeFrame = data.SnoozeCounter >> 6;
		if (snoozeFrame < 1 || snoozeFrame > 3)
		{
			snoozeNode.Visible = false;
			return;
		}

		ushort page = _snoozePages[snoozeFrame - 1];
		if (!_spriteMaterials.TryGetValue(page, out StandardMaterial3D material))
		{
			snoozeNode.Visible = false;
			return;
		}

		Vector3 towardsViewer = viewerPosition - data.Position;
		if (towardsViewer.LengthSquared() > 0f)
			towardsViewer = towardsViewer.Normalized() * SnoozeOverlayDepthOffset;
		else
			towardsViewer = Vector3.Zero;

		snoozeNode.MaterialOverride = material;
		snoozeNode.Position = data.Position + towardsViewer;
		snoozeNode.Rotation = new Vector3(0, _getCameraYRotation(), 0);
		snoozeNode.Visible = true;
	}
	/// <summary>
	/// Calculates which of 8 directional sprites to show based on viewing angle.
	/// Only used for rotated sprites (walking, standing, etc).
	/// Enum values directly correspond to sprite offsets (0-7).
	/// </summary>
	private static ushort CalculateDirectionalSprite(Vector3 actorPosition, ushort baseShape, Vector3 viewerPosition, Direction? actorFacing = 0)
	{
		// Dead actors may have null facing - return base shape (front-facing)
		if (!actorFacing.HasValue)
			return baseShape;
		// Calculate viewing angle from viewer to actor
		Vector3 toActor = actorPosition - viewerPosition;
		float viewAngle = Mathf.Atan2(toActor.Z, toActor.X);  // Angle in radians
		// Convert to [0, 2π) range
		if (viewAngle < 0) viewAngle += Mathf.Tau;
		// Calculate actor's facing angle in Godot coordinates using extension method
		float actorAngle = actorFacing.Value.ToAngle(),
		// Relative angle (viewer's perspective looking at actor, not actor looking at viewer)
		// Add π to flip from "actor to viewer" to "viewer to actor"
		// Swap order to reverse clockwise/counter-clockwise direction
			relativeAngle = actorAngle - viewAngle + Mathf.Pi;
		// Normalize to [0, 2π)
		while (relativeAngle < 0) relativeAngle += Mathf.Tau;
		while (relativeAngle >= Mathf.Tau) relativeAngle -= Mathf.Tau;
		// Map to 8 directions (0-7)
		// Direction 0 = viewing actor from front, 1 = front-right, etc.
		int direction = (int)Mathf.Round(relativeAngle / (Mathf.Tau / 8)) % 8;
		// Return sprite page: BaseShape + direction offset
		return (ushort)(baseShape + direction);
	}
	/// <summary>
	/// Updates all billboard rotations and directional sprites.
	/// Called every frame from _Process().
	/// </summary>
	public override void _Process(double delta)
	{
		float billboardRotation = _getCameraYRotation();
		Vector3 viewerPosition = _getViewerPosition();
		foreach (KeyValuePair<int, MeshInstance3D> kvp in _actorNodes)
		{
			int actorIndex = kvp.Key;
			MeshInstance3D node = kvp.Value;
			// Always update billboard rotation
			node.Rotation = new Vector3(0, billboardRotation, 0);
			// Update directional sprite if this is a rotated sprite
			if (_actorData.TryGetValue(actorIndex, out ActorRenderData data))
			{
				if (data.IsRotated)
				{
					ushort directionalSprite = CalculateDirectionalSprite(
						data.Position, data.BaseShape, viewerPosition, data.Facing);
					node.MaterialOverride = _spriteMaterials[directionalSprite];
				}
				UpdateSnoozeNode(actorIndex, data, viewerPosition);
			}
		}
	}
	/// <summary>
	/// Subscribes to simulator events to automatically show/hide/update actors.
	/// Call this after both Actors and Simulator are initialized.
	/// </summary>
	public void Subscribe(Simulator.Simulator sim)
	{
		ArgumentNullException.ThrowIfNull(sim);
		// Unsubscribe from previous simulator if any
		Unsubscribe();
		_simulator = sim;
		// Subscribe to actor-related events
		_simulator.ActorSpawned += OnActorSpawned;
		_simulator.ActorMoved += OnActorMoved;
		_simulator.ActorSpriteChanged += OnActorSpriteChanged;
		_simulator.ActorPlaySound += OnActorPlaySound;
	}
	/// <summary>
	/// Unsubscribes from simulator events.
	/// </summary>
	public void Unsubscribe()
	{
		if (_simulator is null)
			return;
		_simulator.ActorSpawned -= OnActorSpawned;
		_simulator.ActorMoved -= OnActorMoved;
		_simulator.ActorSpriteChanged -= OnActorSpriteChanged;
		_simulator.ActorPlaySound -= OnActorPlaySound;
		_simulator = null;
	}
	/// <summary>
	/// Handles ActorSpawnedEvent - shows a newly spawned actor.
	/// </summary>
	private void OnActorSpawned(Simulator.ActorSpawnedEvent evt) => ShowActor(evt.ActorIndex, evt.Shape, evt.IsRotated, evt.IsSleeping, evt.SnoozeCounter, evt.TileX, evt.TileY, evt.Facing);
	/// <summary>
	/// Handles ActorMovedEvent - moves an actor to new position.
	/// </summary>
	private void OnActorMoved(Simulator.ActorMovedEvent evt)
	{
		MoveActor(evt.ActorIndex, evt.X, evt.Y);
		// Update facing direction for directional sprites
		if (_actorData.TryGetValue(evt.ActorIndex, out ActorRenderData data))
		{
			data.Facing = evt.Facing;
			_actorData[evt.ActorIndex] = data;
		}
	}
	/// <summary>
	/// Handles ActorSpriteChangedEvent - changes actor's visible sprite.
	/// </summary>
	private void OnActorSpriteChanged(Simulator.ActorSpriteChangedEvent evt) =>
		ChangeActorSprite(evt.ActorIndex, evt.Shape, evt.IsRotated, evt.IsSleeping, evt.SnoozeCounter);
	/// <summary>
	/// Handles ActorDespawnedEvent - hides a despawned actor.
	/// </summary>
	private void OnActorDespawned(Simulator.ActorDespawnedEvent evt) =>
		HideActor(evt.ActorIndex);
	/// <summary>
	/// Handles ActorPlaySoundEvent - plays a sound at the actor's position.
	/// WL_STATE.C:PlaySoundLocActor
	/// </summary>
	private void OnActorPlaySound(Simulator.ActorPlaySoundEvent evt)
	{
		if (!_actorSpeakers.TryGetValue(evt.ActorIndex, out AudioStreamPlayer3D speaker))
		{
			GD.PrintErr($"ERROR: Actor {evt.ActorIndex} has no speaker");
			return;
		}
		// Positional playback is only valid for digitized sound. If digi is unavailable or
		// disabled, fall back to the shared global resolver using the logical sound name.
		string logicalSoundName = SharedAssetManager.ResolveLogicalSoundName(evt.SoundName);
		if (!SharedAssetManager.IsDigitizedSoundEnabled ||
			!SharedAssetManager.TryGetDigiSound(evt.SoundName, out AudioStreamWav sound, out _))
		{
			EventBus.Emit(GameEvent.PlaySound, logicalSoundName);
			return;
		}
		// Stop any currently playing sound before starting the new one
		if (speaker.Playing)
			speaker.Stop();
		// Play the new sound at the actor's speaker
		speaker.Stream = sound;
		speaker.Play();
	}
	/// <summary>
	/// Cleanup when the node is removed from the scene tree.
	/// </summary>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
			Unsubscribe();
		base.Dispose(disposing);
	}
}
