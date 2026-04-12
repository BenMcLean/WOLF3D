-- WL_ACT2.C:T_Stand - calls SightPlayer
-- WL_STATE.C:SightPlayer (lines 1802-1919) - check for player and transition to chase if spotted

-- WL_STATE.C:1807 - Check if reaction timer is counting down
local reactionTimer = GetReactionTimer()
if reactionTimer > 0 then
	-- WL_STATE.C:1812 - Count down reaction time
	SetReactionTimer(reactionTimer - 1)
	if GetReactionTimer() > 0 then
		return  -- Still counting down, not ready to react
	end
	-- Timer just reached 0, fall through to FirstSighting below
end

-- Timer is 0 (never set or just expired)
-- WL_STATE.C:1817-1832 - Check visibility conditions

-- FL_AMBUSH (0x40) - check if in ambush mode
if HasFlag(0x40) then
	-- Ambush actors only activate with line of sight
	if not CheckSight() then
		return
	end
	ClearFlag(0x40)  -- Clear ambush flag
end

-- Check if player is visible
if not CheckSight() then
	return  -- Can't see player, nothing to do
end

-- Player is visible!
if reactionTimer == 0 and GetReactionTimer() == 0 then
	-- WL_STATE.C:1835-1897 - First time seeing player, set reaction time based on actor type
	local actorType = GetActorType()
	local random = US_RndT()  -- WL_DEF.H:US_RndT() - returns 0-255

	if actorType == "Guard" then
		SetReactionTimer(1 + BitShiftRight(random, 2))  -- 1 + random/4
	elseif actorType == "Dog" then
		SetReactionTimer(1 + BitShiftRight(random, 3))  -- 1 + random/8 (fastest!)
	elseif actorType == "SS" then
		SetReactionTimer(1 + BitShiftRight(random, 2))  -- 1 + random/6 (approx)
	elseif actorType == "Hans" then
		SetReactionTimer(1)  -- Immediate reaction
	else
		-- Default for unknown types
		SetReactionTimer(1)
	end

	-- WL_STATE.C:1913 - return false (don't transition yet, wait for timer)
	return
end

-- Timer has expired (was > 0, now == 0) - WL_STATE.C:1916 - call FirstSighting
-- WL_STATE.C:FirstSighting (lines 1560-1784) - Transition based on actor type
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
	-- Default fallback
	ChangeState("s_grdchase1")
end

-- Set attack mode flags (all actor types)
SetFlag(0x10)  -- FL_ATTACKMODE
SetFlag(0x20)  -- FL_FIRSTATTACK
