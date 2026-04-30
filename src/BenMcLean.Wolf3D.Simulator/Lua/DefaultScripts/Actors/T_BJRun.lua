-- WL_ACT2.C:T_BJRun - BJ runs north toward player during victory animation
-- BJRUNSPEED=2048; Speed set on state in XML. ReactionTimer holds tiles remaining.

local move = GetSpeed()

while move > 0 do
	if move < GetDistance() then
		MoveObj(move)
		break
	end

	-- Crossed tile boundary - snap to center
	local tileX = GetTileX()
	local tileY = GetTileY()
	SetPosition((tileX * 65536) + 32768, (tileY * 65536) + 32768)
	move = move - GetDistance()

	-- Decrement tile counter before SelectPathDir so a wall at the final tile
	-- does not prevent the transition to jump states (WL_ACT2.C:T_BJRun --ob->temp1)
	local tilesLeft = GetReactionTimer() - 1
	SetReactionTimer(tilesLeft)
	if tilesLeft <= 0 then
		ChangeState("s_bjjump1")
		return
	end

	-- Advance to next tile (C# SelectPathDir keeps north direction if no patrol arrow)
	SelectPathDir()

	if not HasDirection() then
		return  -- Blocked unexpectedly before counter ran out
	end
end
