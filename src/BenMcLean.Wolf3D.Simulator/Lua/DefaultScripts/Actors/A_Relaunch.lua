-- WL_ACT2.C:A_Relaunch - Angel of Death may chain multiple spark volleys before tiring.
local count = GetReactionTimer() + 1
SetReactionTimer(count)

if count == 3 then
	ChangeState("s_angeltired")
	return
end

if (US_RndT() % 2) == 1 then
	ChangeState("s_angelchase1")
end
