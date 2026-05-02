-- WL_ACT2.C:A_StartDeathCam - triggered when boss enters terminal dead state
-- Called once via TransitionActorState when s_bossdie4 (or equivalent) is entered
local actorType = GetActorType()
if actorType == "Hans" then
	NavigateToMenu("DeathCam_Hans_See")
elseif actorType == "Schabbs" then
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
