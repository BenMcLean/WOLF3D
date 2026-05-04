-- Combined KillActor (score + drops) and A_DeathScream (sound) logic
-- Both original functions switch on ob->obclass
local actorType = GetActorType()

if actorType == "Guard" then
	-- WL_STATE.C:KillActor - guardobj
	AddValue("Score", 100)
	PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2 (4 ammo, not bo_clip's 8)
	-- WL_ACT2.C:A_DeathScream - random death scream
	local sounds = {"DEATHSCREAM1SND", "DEATHSCREAM2SND", "DEATHSCREAM3SND"}
	PlayLocalDigiSound(sounds[US_RndT() % 3 + 1])

elseif actorType == "Officer" then
	-- WL_STATE.C:KillActor - officerobj
	AddValue("Score", 400)
	PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2
	PlayLocalDigiSound("NEINSOVASSND")

elseif actorType == "Mutant" then
	-- WL_STATE.C:KillActor - mutantobj
	AddValue("Score", 700)
	PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2
	PlayLocalDigiSound("AHHHGSND")

elseif actorType == "Dog" then
	-- WL_STATE.C:KillActor - dogobj
	AddValue("Score", 200)
	-- No item drop
	PlayLocalDigiSound("DOGDEATHSND")

elseif actorType == "SS" then
	-- WL_STATE.C:KillActor - ssobj
	AddValue("Score", 500)
	if GetValue("Weapon2") < 1 then
		PlaceItemType(50, "SPR_STAT_27")  -- bo_machinegun
	else
		PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2
	end
	PlayLocalDigiSound("LEBENSND")

elseif actorType == "Hans" then
	-- WL_STATE.C:KillActor - bossobj
	AddValue("Score", 5000)
	PlaceItemType(43, "SPR_STAT_20")  -- bo_key1 (gold key)
	PlayLocalDigiSound("MUTTISND")
	-- DeathCam triggered by A_StartDeathCam on s_bossdie4 (after full animation completes)
end
