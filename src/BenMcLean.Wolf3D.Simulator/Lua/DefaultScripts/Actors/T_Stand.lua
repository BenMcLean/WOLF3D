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
	-- WL_STATE.C:1835-1897 - First time seeing player, set reaction time based on actor type.
	-- Use data-driven Reaction range from XML actor definition.
	SetReactionTimer(GetReactionTime(US_RndT()))
	return
end

-- Timer has expired - WL_STATE.C:1916 - call FirstSighting
-- WL_STATE.C:FirstSighting (lines 1560-1784) - Transition based on actor type.
-- Use data-driven ChaseState and AlertDigiSound from XML actor definition.
local chaseState = GetChaseState()
if chaseState ~= nil and chaseState ~= "" then
	local alertSound = GetAlertSound()
	if alertSound ~= nil and alertSound ~= "" then
		PlayLocalDigiSound(alertSound)
	end
	ChangeState(chaseState)
end

-- Set attack mode flags (all actor types)
SetFlag(0x10)  -- FL_ATTACKMODE
SetFlag(0x20)  -- FL_FIRSTATTACK
