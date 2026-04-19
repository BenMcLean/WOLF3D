-- WL_ACT2.C:A_StartDeathCam - triggered when boss enters terminal dead state
-- Called once via TransitionActorState when s_bossdie4 (or equivalent) is entered
local actorType = GetActorType()
if actorType == "Hans" then
	NavigateToMenu("DeathCam_Hans_See")
end
