-- WL_ACT2.C:SpawnBJVictory - BJ initialization action, runs once at spawn time.
-- Calculates corridor length for T_BJRun, then fires VictoryStarted to teleport player.
-- ReactionTimer (ob->temp1) holds tiles remaining; decremented by T_BJRun each tile crossed.
--
-- Guard: SpawnActorAtTile fires this at spawn, then UpdateActor fires it again when s_bjstart
-- expires. By that second call, PlacePlayer has moved the simulator's player position to the
-- viewing tile, so the runTiles calculation would compute 0 and clobber the correct value.
if GetReactionTimer() ~= 0 then return end

local playerX = GetPlayerTileX()
local playerY = GetPlayerTileY()

-- Walk north from player tile until hitting a non-navigable tile (the viewing position)
-- WL_ACT2.C:SpawnBJVictory: walk north until actorat/tilemap blocks movement
local viewY = playerY
while viewY > 0 and IsTileNavigable(playerX, viewY - 1) do
	viewY = viewY - 1
end

-- Tiles to run: BJ is currently at playerY (after movement init advanced him one tile north).
-- He must cross tiles until reaching viewY, so runTiles = playerY - viewY.
-- WL_ACT2.C:SpawnBJVictory: new->temp1 = 6 (original fixed; dynamic here for any corridor)
local runTiles = playerY - viewY
if runTiles < 1 then runTiles = 1 end
SetReactionTimer(runTiles)

-- Teleport player to viewing tile and fire VictoryStarted event
-- WL_AGENT.C:VictoryTile → gamestate.victoryflag = true
TriggerVictory(playerX, viewY)
