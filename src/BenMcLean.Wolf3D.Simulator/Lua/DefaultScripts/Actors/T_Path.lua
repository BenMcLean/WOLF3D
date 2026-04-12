-- WL_ACT2.C:T_Path (lines 4073-4128)
-- Patrol behavior - actor follows patrol path arrows on map (generic for all actor types)

-- WL_ACT2.C:4078 - if (SightPlayer(ob)) return
-- Implements WL_STATE.C:SightPlayer (lines 1802-1919)

-- Check if reaction timer is counting down
local reactionTimer = GetReactionTimer()
if reactionTimer > 0 then
	-- Count down reaction time
	SetReactionTimer(reactionTimer - 1)
	if GetReactionTimer() > 0 then
		return  -- Still counting down
	end
	-- Timer just reached 0, fall through to FirstSighting below
end

-- Timer is 0 - check visibility
-- FL_AMBUSH (0x40) - check if in ambush mode
if HasFlag(0x40) then
	if not CheckSight() then
		return
	end
	ClearFlag(0x40)
end

-- Check if player is visible
if not CheckSight() then
	-- Continue patrolling (fall through to patrol logic below)
else
	-- Player is visible!
	if reactionTimer == 0 and GetReactionTimer() == 0 then
		-- First time seeing player, set reaction time based on actor type
		local actorType = GetActorType()
		local random = US_RndT()  -- WL_DEF.H:US_RndT() - returns 0-255

		if actorType == "Guard" then
			SetReactionTimer(1 + BitShiftRight(random, 2))  -- 1 + random/4
		elseif actorType == "Dog" then
			SetReactionTimer(1 + BitShiftRight(random, 3))  -- 1 + random/8
		elseif actorType == "SS" then
			SetReactionTimer(1 + BitShiftRight(random, 2))  -- 1 + random/6
		elseif actorType == "Hans" then
			SetReactionTimer(1)  -- Immediate
		else
			SetReactionTimer(1)
		end

		return  -- Don't transition yet, wait for timer
	end

	-- Timer has expired - call FirstSighting
	local actorType = GetActorType()

	if actorType == "Guard" then
		PlayLocalDigiSound("HALTSND")
		ChangeState("s_grdchase1")
	elseif actorType == "Dog" then
		PlayLocalDigiSound("DOGBARKSND")
		ChangeState("s_dogchase1")
	elseif actorType == "SS" then
		PlayLocalDigiSound("SCHUTZADSND")
		ChangeState("s_sschase1")
	elseif actorType == "Hans" then
		PlayLocalDigiSound("GUTENTAGSND")
		ChangeState("s_bosschase1")
	else
		ChangeState("s_grdchase1")
	end

	SetFlag(0x10)  -- FL_ATTACKMODE
	SetFlag(0x20)  -- FL_FIRSTATTACK
	return
end

-- WL_ACT2.C:4081 - if (ob->dir == nodir)
if not HasDirection() then
	SelectPathDir()

	-- WL_ACT2.C:4084 - if (ob->dir == nodir) return
	if not HasDirection() then
		return  -- All movement is blocked
	end
end

-- Calculate movement this tic: move = ob->speed * tics
local move = GetSpeed()

-- Movement loop (original: while(move))
while move > 0 do
	if GetDistance() < 0 then
		-- Waiting for door to open
		-- WL_ACT2.C:4098 - OpenDoor(-ob->distance-1)
		local doorIndex = -GetDistance() - 1
		OpenDoor(doorIndex)
		-- WL_ACT2.C:4099 - if (doorobjlist[doornum].action != dr_open) return
		if not IsDoorOpen(doorIndex) then
			return  -- Door not open yet, wait
		end
		-- WL_ACT2.C:4101 - Door is now open, proceed
		SetDistance(0x10000)  -- TILEGLOBAL
	end

	if move < GetDistance() then
		-- Move partial distance within current tile
		MoveObj(move)
		break
	end

	-- Reached next tile - crossed tile boundary
	-- WL_ACT2.C:4119-4120 - Snap to exact tile center
	-- ob->x = ((long)ob->tilex<<TILESHIFT)+TILEGLOBAL/2;
	-- ob->y = ((long)ob->tiley<<TILESHIFT)+TILEGLOBAL/2;
	local tileX = GetTileX()
	local tileY = GetTileY()
	local centerX = (tileX * 65536) + 32768  -- Tile center in 16.16 fixed-point
	local centerY = (tileY * 65536) + 32768
	SetPosition(centerX, centerY)

	-- WL_ACT2.C:4121 - Subtract the distance we just traveled to reach the center
	move = move - GetDistance()

	-- WL_ACT2.C:4123 - SelectPathDir sets new direction and distance = TILEGLOBAL
	SelectPathDir()

	-- WL_ACT2.C:4125 - if (ob->dir == nodir) return
	if not HasDirection() then
		return  -- All movement is blocked
	end
end
