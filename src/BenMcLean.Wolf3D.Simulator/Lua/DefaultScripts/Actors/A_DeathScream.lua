-- WL6 implementation: combines KillActor (score + drops) and A_DeathScream (sound).
-- WL6 has no per-actor <Script> blocks, so all three effects live here.
-- Games that handle score/drops in per-actor <Script> blocks (e.g. N3D) override
-- this function in their XML with a sound-only version.
-- Both original C functions switch on ob->obclass.
local actorType = GetActorType()

if actorType == "Guard" then
	-- WL_STATE.C:KillActor - guardobj
	AddValue("Score", 100)
	PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2 (4 ammo, not bo_clip's 8)
	-- WL_ACT2.C:A_DeathScream - random death scream
	local sounds = {"DEATHSCREAM1SND", "DEATHSCREAM2SND", "DEATHSCREAM3SND"}
	PlayLocalSound(sounds[US_RndT() % 3 + 1])

elseif actorType == "Officer" then
	-- WL_STATE.C:KillActor - officerobj
	AddValue("Score", 400)
	PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2
	PlayLocalSound("NEINSOVASSND")

elseif actorType == "Mutant" then
	-- WL_STATE.C:KillActor - mutantobj
	AddValue("Score", 700)
	PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2
	PlayLocalSound("AHHHGSND")

elseif actorType == "Dog" then
	-- WL_STATE.C:KillActor - dogobj
	AddValue("Score", 200)
	-- No item drop
	PlayLocalSound("DOGDEATHSND")

elseif actorType == "SS" then
	-- WL_STATE.C:KillActor - ssobj
	AddValue("Score", 500)
	if GetValue("Weapon2") < 1 then
		PlaceItemType(50, "SPR_STAT_27")  -- bo_machinegun
	else
		PlaceItemType(71, "SPR_STAT_26")  -- bo_clip2
	end
	PlayLocalSound("LEBENSND")

elseif actorType == "Hans" then
	-- WL_STATE.C:KillActor - bossobj
	AddValue("Score", 5000)
	PlaceItemType(43, "SPR_STAT_20")  -- bo_key1 (gold key)
	-- WL_ACT2.C:A_DeathScream: bossobj uses SD_PlaySound (global), not PlaySoundLocActor
	PlaySound("MUTTISND")
	-- DeathCam triggered by A_StartDeathCam on s_bossdie4 (after full animation completes)

elseif actorType == "FakeHitler" then
	-- WL_STATE.C:KillActor - fakeobj
	AddValue("Score", 2000)
	-- No item drop
	-- WL_ACT2.C:A_DeathScream: fakeobj uses SD_PlaySound (global)
	PlaySound("HITLERHASND")

elseif actorType == "MechaHitler" then
	-- WL_STATE.C:KillActor - mechahitlerobj
	AddValue("Score", 5000)
	-- No item drop (Real Hitler spawns via A_HitlerMorph on s_mechadie3)
	-- WL_ACT2.C:A_DeathScream: mechahitlerobj uses SD_PlaySound (global)
	PlaySound("SCHEISTSND")

elseif actorType == "Hitler" then
	-- WL_STATE.C:KillActor - realhitlerobj
	AddValue("Score", 5000)
	-- No item drop (DeathCam ends the episode)
	-- WL_ACT2.C:A_DeathScream: realhitlerobj uses SD_PlaySound (global)
	PlaySound("EVASND")
end
