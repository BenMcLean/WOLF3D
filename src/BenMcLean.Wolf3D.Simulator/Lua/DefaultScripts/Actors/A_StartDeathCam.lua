-- WL_ACT2.C:A_StartDeathCam - triggered when boss enters terminal dead state
-- Hans does not use the DeathCam flow in Wolf3D; he only plays his death
-- sound and drops the gold key from A_DeathScream/KillActor behavior.
-- Called once via TransitionActorState when the supported boss terminal dead
-- state is entered.
local actorType = GetActorType()
if actorType == "Schabbs" then
	NavigateToMenu("DeathCam_Schabbs_See")
elseif actorType == "Hitler" then
	NavigateToMenu("DeathCam_Hitler_See")
elseif actorType == "Gretel" then
	NavigateToMenu("DeathCam_Gretel_See")
elseif actorType == "Giftmacher" then
	NavigateToMenu("DeathCam_Giftmacher_See")
elseif actorType == "FatFace" then
	NavigateToMenu("DeathCam_FatFace_See")
end
