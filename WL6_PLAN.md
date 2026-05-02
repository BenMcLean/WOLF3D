# WL6 Support Implementation Plan

Starting point: copy `games/WL1.xml` to `games/WL6.xml`, then apply each change below in order.

---

## 1. Game Root Element

**File:** `games/WL6.xml` (root `<Game>` tag)

| Attribute | WL1 value | WL6 value |
|---|---|---|
| `Name` | `Wolfenstein 3-D Shareware` | `Wolfenstein 3-D` |
| `Version` | `1.4` | *(remove attribute)* |
| `Path` | `WL1` | `WL6` |
| `Extension` | `WL1` | `WL6` |
| `HighScores` | `CONFIG.WL1` | `CONFIG.WL6` |

---

## 2. VgaGraph — File References

Change `<VgaGraph>` attributes:
- `VgaDict="VGADICT.WL1"` → `VGADICT.WL6`
- `VgaGraph="VGAGRAPH.WL1"` → `VGAGRAPH.WL6`
- `VgaHead="VGAHEAD.WL1"` → `VGAHEAD.WL6`

---

## 3. VgaGraph — Complete Pics Replacement

**Source:** `wolf3d/GFXV_WL6.H`

WL1 and WL6 have entirely different pic enumerations. Replace the entire `<Pics Start="3">` block.

WL1 has shareware-only pics that don't exist in WL6 (e.g. `H_KEYBOARDPIC`, `H_JOYPIC`, `H_HEALPIC`, `H_TREASUREPIC`, `H_GUNPIC`, `H_KEYPIC`). WL6 adds `C_EPISODE4-6PIC`, `C_TIMECODEPIC`, `C_LEVELPIC`, `C_NAMEPIC`, `C_SCOREPIC`, `C_JOY1PIC`, `C_JOY2PIC`.

The `Number` attribute is 0-based pic index; chunk = `Start` (3) + `Number`. All values derived directly from `GFXV_WL6.H`.

```xml
<Pics Start="3">
	<!-- Help screen lumps (chunks 3-9) -->
	<Pic Number="0"  Name="H_BJPIC" />
	<Pic Number="1"  Name="H_CASTLEPIC" />
	<Pic Number="2"  Name="H_BLAZEPIC" />
	<Pic Number="3"  Name="H_TOPWINDOWPIC" />
	<Pic Number="4"  Name="H_LEFTWINDOWPIC" />
	<Pic Number="5"  Name="H_RIGHTWINDOWPIC" />
	<Pic Number="6"  Name="H_BOTTOMINFOPIC" />
	<!-- Control screen lumps (chunks 10-42) -->
	<Pic Number="7"  Name="C_OPTIONSPIC" />
	<Pic Number="8"  Name="C_CURSOR1PIC" />
	<Pic Number="9"  Name="C_CURSOR2PIC" />
	<Pic Number="10" Name="C_NOTSELECTEDPIC" />
	<Pic Number="11" Name="C_SELECTEDPIC" />
	<Pic Number="12" Name="C_FXTITLEPIC" />
	<Pic Number="13" Name="C_DIGITITLEPIC" />
	<Pic Number="14" Name="C_MUSICTITLEPIC" />
	<Pic Number="15" Name="C_MOUSELBACKPIC" />
	<Pic Number="16" Name="C_BABYMODEPIC" />
	<Pic Number="17" Name="C_EASYPIC" />
	<Pic Number="18" Name="C_NORMALPIC" />
	<Pic Number="19" Name="C_HARDPIC" />
	<Pic Number="20" Name="C_LOADSAVEDISKPIC" />
	<Pic Number="21" Name="C_DISKLOADING1PIC" />
	<Pic Number="22" Name="C_DISKLOADING2PIC" />
	<Pic Number="23" Name="C_CONTROLPIC" />
	<Pic Number="24" Name="C_CUSTOMIZEPIC" />
	<Pic Number="25" Name="C_LOADGAMEPIC" />
	<Pic Number="26" Name="C_SAVEGAMEPIC" />
	<Pic Number="27" Name="C_EPISODE1PIC" />
	<Pic Number="28" Name="C_EPISODE2PIC" />
	<Pic Number="29" Name="C_EPISODE3PIC" />
	<Pic Number="30" Name="C_EPISODE4PIC" />
	<Pic Number="31" Name="C_EPISODE5PIC" />
	<Pic Number="32" Name="C_EPISODE6PIC" />
	<Pic Number="33" Name="C_CODEPIC" />
	<Pic Number="34" Name="C_TIMECODEPIC" />
	<Pic Number="35" Name="C_LEVELPIC" />
	<Pic Number="36" Name="C_NAMEPIC" />
	<Pic Number="37" Name="C_SCOREPIC" />
	<Pic Number="38" Name="C_JOY1PIC" />
	<Pic Number="39" Name="C_JOY2PIC" />
	<!-- Level-end screen lumps (chunks 43-85) -->
	<Pic Number="40" Name="L_GUYPIC" />
	<Pic Number="41" Name="L_COLONPIC" Character=":" />
	<Pic Number="42" Name="L_NUM0PIC" Character="0" />
	<Pic Number="43" Name="L_NUM1PIC" Character="1" />
	<Pic Number="44" Name="L_NUM2PIC" Character="2" />
	<Pic Number="45" Name="L_NUM3PIC" Character="3" />
	<Pic Number="46" Name="L_NUM4PIC" Character="4" />
	<Pic Number="47" Name="L_NUM5PIC" Character="5" />
	<Pic Number="48" Name="L_NUM6PIC" Character="6" />
	<Pic Number="49" Name="L_NUM7PIC" Character="7" />
	<Pic Number="50" Name="L_NUM8PIC" Character="8" />
	<Pic Number="51" Name="L_NUM9PIC" Character="9" />
	<Pic Number="52" Name="L_PERCENTPIC" Character="%" />
	<Pic Number="53" Name="L_APIC" Character="A" />
	<Pic Number="54" Name="L_BPIC" Character="B" />
	<Pic Number="55" Name="L_CPIC" Character="C" />
	<Pic Number="56" Name="L_DPIC" Character="D" />
	<Pic Number="57" Name="L_EPIC" Character="E" />
	<Pic Number="58" Name="L_FPIC" Character="F" />
	<Pic Number="59" Name="L_GPIC" Character="G" />
	<Pic Number="60" Name="L_HPIC" Character="H" />
	<Pic Number="61" Name="L_IPIC" Character="I" />
	<Pic Number="62" Name="L_JPIC" Character="J" />
	<Pic Number="63" Name="L_KPIC" Character="K" />
	<Pic Number="64" Name="L_LPIC" Character="L" />
	<Pic Number="65" Name="L_MPIC" Character="M" />
	<Pic Number="66" Name="L_NPIC" Character="N" />
	<Pic Number="67" Name="L_OPIC" Character="O" />
	<Pic Number="68" Name="L_PPIC" Character="P" />
	<Pic Number="69" Name="L_QPIC" Character="Q" />
	<Pic Number="70" Name="L_RPIC" Character="R" />
	<Pic Number="71" Name="L_SPIC" Character="S" />
	<Pic Number="72" Name="L_TPIC" Character="T" />
	<Pic Number="73" Name="L_UPIC" Character="U" />
	<Pic Number="74" Name="L_VPIC" Character="V" />
	<Pic Number="75" Name="L_WPIC" Character="W" />
	<Pic Number="76" Name="L_XPIC" Character="X" />
	<Pic Number="77" Name="L_YPIC" Character="Y" />
	<Pic Number="78" Name="L_ZPIC" Character="Z" />
	<Pic Number="79" Name="L_EXPOINTPIC" Character="!" />
	<Pic Number="80" Name="L_APOSTROPHEPIC" Character="'" />
	<Pic Number="81" Name="L_GUY2PIC" />
	<Pic Number="82" Name="L_BJWINSPIC" />
	<!-- Standalone pics (chunks 86-90) -->
	<Pic Number="83" Name="STATUSBARPIC" />
	<Pic Number="84" Name="TITLEPIC" />
	<Pic Number="85" Name="PG13PIC" />
	<Pic Number="86" Name="CREDITSPIC" />
	<Pic Number="87" Name="HIGHSCORESPIC" />
	<!-- Latch pics / in-game HUD (chunks 91-134) -->
	<Pic Number="88" Name="KNIFEPIC" />
	<Pic Number="89" Name="GUNPIC" />
	<Pic Number="90" Name="MACHINEGUNPIC" />
	<Pic Number="91" Name="GATLINGGUNPIC" />
	<Pic Number="92" Name="NOKEYPIC" />
	<Pic Number="93" Name="GOLDKEYPIC" />
	<Pic Number="94" Name="SILVERKEYPIC" />
	<Pic Number="95" Name="N_BLANKPIC" />
	<Pic Number="96" Name="N_0PIC" Character="0" />
	<Pic Number="97" Name="N_1PIC" Character="1" />
	<Pic Number="98" Name="N_2PIC" Character="2" />
	<Pic Number="99" Name="N_3PIC" Character="3" />
	<Pic Number="100" Name="N_4PIC" Character="4" />
	<Pic Number="101" Name="N_5PIC" Character="5" />
	<Pic Number="102" Name="N_6PIC" Character="6" />
	<Pic Number="103" Name="N_7PIC" Character="7" />
	<Pic Number="104" Name="N_8PIC" Character="8" />
	<Pic Number="105" Name="N_9PIC" Character="9" />
	<Pic Number="106" Name="FACE1APIC" />
	<Pic Number="107" Name="FACE1BPIC" />
	<Pic Number="108" Name="FACE1CPIC" />
	<Pic Number="109" Name="FACE2APIC" />
	<Pic Number="110" Name="FACE2BPIC" />
	<Pic Number="111" Name="FACE2CPIC" />
	<Pic Number="112" Name="FACE3APIC" />
	<Pic Number="113" Name="FACE3BPIC" />
	<Pic Number="114" Name="FACE3CPIC" />
	<Pic Number="115" Name="FACE4APIC" />
	<Pic Number="116" Name="FACE4BPIC" />
	<Pic Number="117" Name="FACE4CPIC" />
	<Pic Number="118" Name="FACE5APIC" />
	<Pic Number="119" Name="FACE5BPIC" />
	<Pic Number="120" Name="FACE5CPIC" />
	<Pic Number="121" Name="FACE6APIC" />
	<Pic Number="122" Name="FACE6BPIC" />
	<Pic Number="123" Name="FACE6CPIC" />
	<Pic Number="124" Name="FACE7APIC" />
	<Pic Number="125" Name="FACE7BPIC" />
	<Pic Number="126" Name="FACE7CPIC" />
	<Pic Number="127" Name="FACE8APIC" />
	<Pic Number="128" Name="GOTGATLINGPIC" />
	<Pic Number="129" Name="MUTANTBJPIC" />
	<Pic Number="130" Name="PAUSEDPIC" />
	<Pic Number="131" Name="GETPSYCHEDPIC" />
</Pics>
```

**Note on pic name references:** Any menu `<Picture Name="...">` or StatusBar `Have="..."` / `Empty="..."` that references pic names from WL1 that no longer exist in WL6 (e.g. any `H_KEYBOARD*`, `H_JOY*`, `H_HEAL*`, `H_TREASURE*`, `H_GUN*`, `H_KEY*`) must be updated or removed. Verify each `<Picture>` reference in the Menus section compiles against the new pic list.

This verification should not be done manually: it should be scripted and not just for pics but for asset references in general. The script should parse the XML and check every attribute which is a reference to an asset to ensure it references an asset that actually exists in the section for that asset type.

---

## 4. VgaGraph — TextChunks

**Source:** `GFXV_WL6.H` (STARTEXTERNS=136, T_HELPART=138 through T_ENDART6=148)

Replace the entire `<TextChunks>` block. The `Number` is the absolute chunk number.

```xml
<TextChunks>
	<TextChunk Name="T_HELPART"  Number="138" />
	<TextChunk Name="T_ENDART1"  Number="143" />
	<TextChunk Name="T_ENDART2"  Number="144" />
	<TextChunk Name="T_ENDART3"  Number="145" />
	<TextChunk Name="T_ENDART4"  Number="146" />
	<TextChunk Name="T_ENDART5"  Number="147" />
	<TextChunk Name="T_ENDART6"  Number="148" />
</TextChunks>
```

Note: `T_DEMO0`–`T_DEMO3` (chunks 139–142) exist in the WL6 data files but are not declared here — demos are not implemented for VR.

---

## 5. Menus — Main Menu

Remove the "Read This!" item (shareware-only — navigated to `T_HELPART`):

```xml
<!-- DELETE this item: -->
<MenuItem Text="Read This!">
    NavigateToArticle("ReadThis")
</MenuItem>
```

---

## 6. Menus — Episodes Menu

Episodes 2–6 in WL1 show an "order now" message. In WL6 they navigate normally. Replace items 2–6:

```xml
<!-- REPLACE the five disabled episodes with: -->
<MenuItem Text="Episode 2&#10;Operation: Eisenfaust">
    SetEpisode(1)
    NavigateToMenu("NewGame")
</MenuItem>
<MenuItem Text="Episode 3&#10;Die, Fuhrer, Die!">
    SetEpisode(2)
    NavigateToMenu("NewGame")
</MenuItem>
<MenuItem Text="Episode 4&#10;A Dark Secret">
    SetEpisode(3)
    NavigateToMenu("NewGame")
</MenuItem>
<MenuItem Text="Episode 5&#10;Trail of the Madman">
    SetEpisode(4)
    NavigateToMenu("NewGame")
</MenuItem>
<MenuItem Text="Episode 6&#10;Confrontation">
    SetEpisode(5)
    NavigateToMenu("NewGame")
</MenuItem>
```

---

## 7. Menus — Victory Menu (per-episode endings)

WL1 `<Pause>` in Victory hardcodes `NavigateToArticle("EndArt1")`. WL6 needs to show the correct `T_ENDART1`–`T_ENDART6` for the current episode. Replace the `<Pause>` line:

```xml
<!-- REPLACE: -->
<Pause>NavigateToArticle("EndArt1")</Pause>
<!-- WITH: -->
<Pause>NavigateToArticle("EndArt" .. GetEpisode())</Pause>
```

Also change `Music="URAHERO_MUS"` — episode 6 uses a different victory fanfare. Investigate what WL_INTER.C uses per episode. Placeholder: keep `URAHERO_MUS` for episodes 1–5 and `VICMARCH_MUS` for episode 6 (verify against original).

---

## 8. Menus — New Boss DeathCam Menus

WL1 has `DeathCam_Hans_See` and `DeathCam_Hans_Cam`. Add equivalent pairs for every WL6 boss. Pattern is the same — a "LET'S SEE THAT AGAIN!" screen followed by a camera replay menu. Naming convention: `DeathCam_<BossName>_See` / `DeathCam_<BossName>_Cam`.

Bosses to add (5 pairs):
- `DeathCam_Schabbs_See` / `DeathCam_Schabbs_Cam`
- `DeathCam_Hitler_See` / `DeathCam_Hitler_Cam` (fires only when Real Hitler dies — the Mecha→Real Hitler phase transition has no DeathCam)
- `DeathCam_Gretel_See` / `DeathCam_Gretel_Cam`
- `DeathCam_Giftmacher_See` / `DeathCam_Giftmacher_Cam`
- `DeathCam_FatFace_See` / `DeathCam_FatFace_Cam`

Copy the `DeathCam_Hans_See`/`Cam` structure exactly.

Note: Hans isn't really supposed to have the DeathCam but it was implemented just to show how the DeathCam will be reperesented in VR. After the other bosses get their DeathCams and are confirmed working, Hans DeathCam can be removed.

---

## 9. Audio — File References and Sound 8 Rename

Change `<Audio>` file attributes:
- `AudioHead="AUDIOHED.WL1"` → `AUDIOHED.WL6`
- `AudioT="AUDIOT.WL1"` → `AUDIOT.WL6`

The WL1.xml audio structure is already WL6-compatible (`StartAdlibSounds="87"`, `StartMusic="261"`, all 87 sounds, all 27 music tracks including `PACMAN_MUS`). Only one sound needs renaming:

```xml
<!-- CHANGE: -->
<Sound Number="8" Name="FIRESND" />
<!-- TO: -->
<Sound Number="8" Name="SCHABBSTHROWSND" />
```

This is a semantic rename only; the audio data at index 8 is the same binary chunk in both WL1 and WL6.

---

## 10. Maps — File References and Expand to 60 Maps

**Source:** `wolf3d/MAPSWL6.H`

Change `<Maps>` file attributes:
- `MapHead="MAPHEAD.WL1"` → `MAPHEAD.WL6`
- `GameMaps="GAMEMAPS.WL1"` → `GAMEMAPS.WL6`

The 10 WL1 maps (Episode 1) stay unchanged. Add 50 maps for episodes 2–6 plus the special map 60.

**Data needed before writing:** Look up per-level `Par` times and `Song` assignments in `wolf3d/WL_GAME.C` (the `levelinfo[]` array). The map table below uses `TODO` where that data needs to be filled in. Ceiling/Ground/Border/Floor values are `TODO` pending verification against the original — the old draft WL6.xml used `Ground="25" Ceiling="29" Border="127"` throughout; verify whether this is correct for all episodes.

```xml
<!-- EPISODE 2 (maps 10-19) -->
<Map Number="10" Name="WOLF2_MAP1_MAP"  Episode="2" Floor="1"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="11" Name="WOLF2_MAP2_MAP"  Episode="2" Floor="2"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="12" Name="WOLF2_MAP3_MAP"  Episode="2" Floor="3"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="13" Name="WOLF2_MAP4_MAP"  Episode="2" Floor="4"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="14" Name="WOLF2_MAP5_MAP"  Episode="2" Floor="5"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="15" Name="WOLF2_MAP6_MAP"  Episode="2" Floor="6"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="16" Name="WOLF2_MAP7_MAP"  Episode="2" Floor="7"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="17" Name="WOLF2_MAP8_MAP"  Episode="2" Floor="8"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<Map Number="18" Name="WOLF2_BOSS_MAP"  Episode="2" Floor="9"  Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" />
<Map Number="19" Name="WOLF2_SECRET_MAP" Episode="2" Floor="10" Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" ElevatorTo="TODO" />

<!-- EPISODE 3 (maps 20-29) -->
<Map Number="20" Name="WOLF3_MAP1_MAP"  Episode="3" Floor="1"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<!-- ... (same pattern through 29) -->
<Map Number="28" Name="WOLF3_BOSS_MAP"  Episode="3" Floor="9"  Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" />
<Map Number="29" Name="WOLF3_SECRET_MAP" Episode="3" Floor="10" Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" ElevatorTo="TODO" />

<!-- EPISODE 4 (maps 30-39) — note: MAPSWL6.H uses WOLF4_MAP_1_MAP (underscore before number) -->
<Map Number="30" Name="WOLF4_MAP_1_MAP" Episode="4" Floor="1"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<!-- ... (same pattern through 39) -->
<Map Number="38" Name="WOLF4_BOSS_MAP"  Episode="4" Floor="9"  Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" />
<Map Number="39" Name="WOLF4_SECRET_MAP" Episode="4" Floor="10" Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" ElevatorTo="TODO" />

<!-- EPISODE 5 (maps 40-49) — note: MAPSWL6.H uses WOLF5_MAP_1_MAP -->
<Map Number="40" Name="WOLF5_MAP_1_MAP" Episode="5" Floor="1"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<!-- ... (same pattern through 49) -->
<Map Number="48" Name="WOLF5_BOSS_MAP"  Episode="5" Floor="9"  Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" />
<Map Number="49" Name="WOLF5_SECRET_MAP" Episode="5" Floor="10" Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" ElevatorTo="TODO" />

<!-- EPISODE 6 (maps 50-59) — note: MAPSWL6.H uses WOLF6_MAP_1_MAP -->
<Map Number="50" Name="WOLF6_MAP_1_MAP" Episode="6" Floor="1"  Ground="TODO" Ceiling="TODO" Border="127" Par="TODO" Song="TODO" />
<!-- ... (same pattern through 59) -->
<Map Number="58" Name="WOLF6_BOSS_MAP"  Episode="6" Floor="9"  Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" />
<Map Number="59" Name="WOLF6_SECRET_MAP" Episode="6" Floor="10" Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" ElevatorTo="TODO" />

<!-- SPECIAL: alternate path level (MAPSWL6.H: MAP4L10PATH_MAP=60) -->
<Map Number="60" Name="MAP4L10PATH_MAP" Episode="4" Floor="10" Ground="TODO" Ceiling="TODO" Border="127" Song="TODO" />
```

**To fill in the TODOs:** Read `wolf3d/WL_GAME.C` — look for the `levelinfo[]` array which contains par times and song assignments indexed by map number. Ceiling/Ground tile codes can be cross-referenced against the VSwap `<Walls FloorCodeFirst>` range.

---

## 11. VSwap — File Reference

Change `<VSwap Name="VSWAP.WL1" ...>` to `Name="VSWAP.WL6"`.

---

## 12. VSwap — New Boss Actor Objects

**Source:** `wolf3d/WL_DEF.H` (sprite enum), `wolf3d/WL_ACT2.C` (boss spawn logic)

WL6 adds 5 new bosses, one per added episode. Each boss needs an `<Actor>` spawn object in the `<Objects>` section and a corresponding `<Actor>` + `<State>` block in `<Actors>`. The `Page` numbers for boss spawn objects are in the VSWAP sprite page layout — **these page numbers must be verified** by counting through the WL6 VSWAP page structure. The WL1.xml Hans object (Number="214") is the reference point.

Boss episode assignment:
| Episode | Boss | Sprite prefix | WL1.xml analogue |
|---|---|---|---|
| 1 | Hans Grosse | `SPR_BOSS_*` | Already in WL1 |
| 2 | Dr. Schabbs | `SPR_SCHABB_*` + `SPR_HYPO*` | New |
| 3 | Adolf Hitler | `SPR_MECHA_*` → `SPR_HITLER_*` (two-phase) | New |
| 4 | Gretel Grosse | `SPR_GRETEL_*` | New |
| 5 | Otto Giftmacher | `SPR_GIFT_*` | New |
| 6 | General Fettgesicht | `SPR_FAT_*` | New |

### 12a. Schabbs Actor

Schabbs differs from Hans: he throws hypodermic needles (projectiles using `SPR_HYPO1-4`) rather than shooting hitscan. His shoot action spawns a projectile actor.

```xml
<!-- In <Objects> section, after Hans spawn object: -->
<Actor Number="TODO" Name="Schabbs" Page="TODO" />

<!-- In <Actors> section: -->
<Actor Name="Schabbs" HP="850,950,1050,1200" Reaction="1"
       AlertDigiSound="SCHABBSHASND"
       Stand="s_schabbstand" Chase="s_schabbchase1"
       Attack="s_schabbshoot1" Death="s_schabbdie1" Ambush="true" />
<State Name="s_schabbstand"   Shape="SPR_SCHABB_W1"   Tics="0"  Think="T_Stand"  Speed="0"   Next="s_schabbstand" />
<State Name="s_schabbchase1"  Shape="SPR_SCHABB_W1"   Tics="10" Think="T_Schabb" Speed="512" Next="s_schabbchase1s" />
<State Name="s_schabbchase1s" Shape="SPR_SCHABB_W1"   Tics="3"                   Speed="512" Next="s_schabbchase2" />
<State Name="s_schabbchase2"  Shape="SPR_SCHABB_W2"   Tics="8"  Think="T_Schabb" Speed="512" Next="s_schabbchase3" />
<State Name="s_schabbchase3"  Shape="SPR_SCHABB_W3"   Tics="10" Think="T_Schabb" Speed="512" Next="s_schabbchase3s" />
<State Name="s_schabbchase3s" Shape="SPR_SCHABB_W3"   Tics="3"                   Speed="512" Next="s_schabbchase4" />
<State Name="s_schabbchase4"  Shape="SPR_SCHABB_W4"   Tics="8"  Think="T_Schabb" Speed="512" Next="s_schabbchase1" />
<!-- DeathCam replay state — A_StartDeathCam transitions into this for schabbobj -->
<State Name="s_schabbdeathcam" Shape="SPR_SCHABB_W1"  Tics="1"  Speed="0"        Next="s_schabbdie1" />
<State Name="s_schabbdie1"    Shape="SPR_SCHABB_W1"   Tics="10" Speed="0"        Action="A_DeathScream" Alive="false" Next="s_schabbdie2" />
<State Name="s_schabbdie2"    Shape="SPR_SCHABB_W1"   Tics="10" Speed="0"        Alive="false" Mark="false" Next="s_schabbdie3" />
<State Name="s_schabbdie3"    Shape="SPR_SCHABB_DIE1" Tics="10" Speed="0"        Alive="false" Mark="false" Next="s_schabbdie4" />
<State Name="s_schabbdie4"    Shape="SPR_SCHABB_DIE2" Tics="10" Speed="0"        Alive="false" Mark="false" Next="s_schabbdie5" />
<State Name="s_schabbdie5"    Shape="SPR_SCHABB_DIE3" Tics="10" Speed="0"        Alive="false" Mark="false" Next="s_schabbdie6" />
<State Name="s_schabbdie6"    Shape="SPR_SCHABB_DEAD" Tics="20" Speed="0"        Action="A_StartDeathCam" Next="s_schabbdie6" Alive="false" Mark="false" />
<!-- Only 2 shoot states: Schabbs throws one hypo then returns to chase -->
<State Name="s_schabbshoot1"  Shape="SPR_SCHABB_SHOOT1" Tics="30" Speed="0"     Next="s_schabbshoot2" />
<State Name="s_schabbshoot2"  Shape="SPR_SCHABB_SHOOT2" Tics="10" Speed="0"     Action="T_SchabbThrow" Next="s_schabbchase1" />
<!-- Hypodermic projectile states (shared with Giftmacher) -->
<State Name="s_needle1" Shape="SPR_HYPO1" Tics="6" Think="T_Projectile" Speed="TODO" Next="s_needle2" />
<State Name="s_needle2" Shape="SPR_HYPO2" Tics="6" Think="T_Projectile" Speed="TODO" Next="s_needle3" />
<State Name="s_needle3" Shape="SPR_HYPO3" Tics="6" Think="T_Projectile" Speed="TODO" Next="s_needle4" />
<State Name="s_needle4" Shape="SPR_HYPO4" Tics="6" Think="T_Projectile" Speed="TODO" Next="s_needle1" />
```

**TODO:** Verify projectile speed for needle states against `wolf3d/WL_ACT2.C:T_Projectile`.

### 12b. Hitler (Two-Phase Boss)

Episode 3's Hitler is a two-phase fight: Mecha-Hitler (robot suit) first, then Real Hitler emerges after the mech is destroyed. `A_HitlerMorph` fires on `s_mechadie3` (the third death frame — not the dead state) and spawns the real Hitler actor. The dead mech just loops with no action. No DeathCam for the phase transition — only Real Hitler's final death triggers `A_StartDeathCam`.

```xml
<!-- In <Objects>: -->
<Actor Number="TODO" Name="Hitler" Page="TODO" />

<!-- Phase 1: Mecha-Hitler -->
<Actor Name="MechaHitler" HP="800,900,1000,1200" Reaction="1"
       AlertDigiSound="HITLERHASND"
       Stand="s_mechastand" Chase="s_mechachase1"
       Attack="s_mechashoot1" Death="s_mechadie1" Ambush="true" />
<State Name="s_mechastand"   Shape="SPR_MECHA_W1"     Tics="0"  Think="T_Stand" Speed="0"   Next="s_mechastand" />
<!-- A_MechaSound plays MECHSTEPSND (fires on chase1 and chase3, not the 's' pause states) -->
<State Name="s_mechachase1"  Shape="SPR_MECHA_W1"     Tics="10" Think="T_Chase" Action="A_MechaSound" Speed="512" Next="s_mechachase1s" />
<State Name="s_mechachase1s" Shape="SPR_MECHA_W1"     Tics="6"                  Speed="512" Next="s_mechachase2" />
<State Name="s_mechachase2"  Shape="SPR_MECHA_W2"     Tics="8"  Think="T_Chase" Speed="512" Next="s_mechachase3" />
<State Name="s_mechachase3"  Shape="SPR_MECHA_W3"     Tics="10" Think="T_Chase" Action="A_MechaSound" Speed="512" Next="s_mechachase3s" />
<State Name="s_mechachase3s" Shape="SPR_MECHA_W3"     Tics="6"                  Speed="512" Next="s_mechachase4" />
<State Name="s_mechachase4"  Shape="SPR_MECHA_W4"     Tics="8"  Think="T_Chase" Speed="512" Next="s_mechachase1" />
<!-- A_HitlerMorph fires on die3 (not the dead state) — spawns Real Hitler, leaves dead mech in place -->
<State Name="s_mechadie1"   Shape="SPR_MECHA_DIE1"   Tics="10" Speed="0" Action="A_DeathScream" Alive="false" Next="s_mechadie2" />
<State Name="s_mechadie2"   Shape="SPR_MECHA_DIE2"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_mechadie3" />
<State Name="s_mechadie3"   Shape="SPR_MECHA_DIE3"   Tics="10" Speed="0" Action="A_HitlerMorph" Alive="false" Mark="false" Next="s_mechadie4" />
<State Name="s_mechadie4"   Shape="SPR_MECHA_DEAD"   Tics="0"  Speed="0" Alive="false" Mark="false" Next="s_mechadie4" />
<!-- 6 shoot states (not 8) -->
<State Name="s_mechashoot1" Shape="SPR_MECHA_SHOOT1" Tics="30" Speed="0" Next="s_mechashoot2" />
<State Name="s_mechashoot2" Shape="SPR_MECHA_SHOOT2" Tics="10" Speed="0" Action="T_Shoot" Next="s_mechashoot3" />
<State Name="s_mechashoot3" Shape="SPR_MECHA_SHOOT3" Tics="10" Speed="0" Action="T_Shoot" Next="s_mechashoot4" />
<State Name="s_mechashoot4" Shape="SPR_MECHA_SHOOT2" Tics="10" Speed="0" Action="T_Shoot" Next="s_mechashoot5" />
<State Name="s_mechashoot5" Shape="SPR_MECHA_SHOOT3" Tics="10" Speed="0" Action="T_Shoot" Next="s_mechashoot6" />
<State Name="s_mechashoot6" Shape="SPR_MECHA_SHOOT2" Tics="10" Speed="0" Action="T_Shoot" Next="s_mechachase1" />

<!-- Phase 2: Real Hitler (spawned by A_HitlerMorph) -->
<Actor Name="Hitler" HP="500,700,800,1000" Reaction="1"
       AlertDigiSound="HITLERHASND"
       Stand="s_hitlerstand" Chase="s_hitlerchase1"
       Attack="s_hitlershoot1" Death="s_hitlerdie1" Ambush="true" />
<State Name="s_hitlerstand"    Shape="SPR_HITLER_W1"     Tics="0"  Think="T_Stand" Speed="0"    Next="s_hitlerstand" />
<State Name="s_hitlerchase1"   Shape="SPR_HITLER_W1"     Tics="6"  Think="T_Chase" Speed="1024" Next="s_hitlerchase1s" />
<State Name="s_hitlerchase1s"  Shape="SPR_HITLER_W1"     Tics="4"                  Speed="1024" Next="s_hitlerchase2" />
<State Name="s_hitlerchase2"   Shape="SPR_HITLER_W2"     Tics="2"  Think="T_Chase" Speed="1024" Next="s_hitlerchase3" />
<State Name="s_hitlerchase3"   Shape="SPR_HITLER_W3"     Tics="6"  Think="T_Chase" Speed="1024" Next="s_hitlerchase3s" />
<State Name="s_hitlerchase3s"  Shape="SPR_HITLER_W3"     Tics="4"                  Speed="1024" Next="s_hitlerchase4" />
<State Name="s_hitlerchase4"   Shape="SPR_HITLER_W4"     Tics="2"  Think="T_Chase" Speed="1024" Next="s_hitlerchase1" />
<!-- DeathCam replay state — A_StartDeathCam transitions into this for realhitlerobj -->
<State Name="s_hitlerdeathcam" Shape="SPR_HITLER_W1"     Tics="10" Speed="0"       Next="s_hitlerdie1" />
<!-- 10 death states; die1=A_DeathScream, die3=A_Slurpie (plays SLURPIESND), die10=A_StartDeathCam -->
<State Name="s_hitlerdie1"    Shape="SPR_HITLER_W1"     Tics="1"  Speed="0" Action="A_DeathScream" Alive="false" Next="s_hitlerdie2" />
<State Name="s_hitlerdie2"    Shape="SPR_HITLER_W1"     Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie3" />
<State Name="s_hitlerdie3"    Shape="SPR_HITLER_DIE1"   Tics="10" Speed="0" Action="A_Slurpie" Alive="false" Mark="false" Next="s_hitlerdie4" />
<State Name="s_hitlerdie4"    Shape="SPR_HITLER_DIE2"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie5" />
<State Name="s_hitlerdie5"    Shape="SPR_HITLER_DIE3"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie6" />
<State Name="s_hitlerdie6"    Shape="SPR_HITLER_DIE4"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie7" />
<State Name="s_hitlerdie7"    Shape="SPR_HITLER_DIE5"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie8" />
<State Name="s_hitlerdie8"    Shape="SPR_HITLER_DIE6"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie9" />
<State Name="s_hitlerdie9"    Shape="SPR_HITLER_DIE7"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_hitlerdie10" />
<State Name="s_hitlerdie10"   Shape="SPR_HITLER_DEAD"   Tics="20" Speed="0" Action="A_StartDeathCam" Next="s_hitlerdie10" Alive="false" Mark="false" />
<!-- 6 shoot states -->
<State Name="s_hitlershoot1" Shape="SPR_HITLER_SHOOT1" Tics="30" Speed="0" Next="s_hitlershoot2" />
<State Name="s_hitlershoot2" Shape="SPR_HITLER_SHOOT2" Tics="10" Speed="0" Action="T_Shoot" Next="s_hitlershoot3" />
<State Name="s_hitlershoot3" Shape="SPR_HITLER_SHOOT3" Tics="10" Speed="0" Action="T_Shoot" Next="s_hitlershoot4" />
<State Name="s_hitlershoot4" Shape="SPR_HITLER_SHOOT2" Tics="10" Speed="0" Action="T_Shoot" Next="s_hitlershoot5" />
<State Name="s_hitlershoot5" Shape="SPR_HITLER_SHOOT3" Tics="10" Speed="0" Action="T_Shoot" Next="s_hitlershoot6" />
<State Name="s_hitlershoot6" Shape="SPR_HITLER_SHOOT2" Tics="10" Speed="0" Action="T_Shoot" Next="s_hitlerchase1" />
```

**TODO:** Verify Hitler HP values against `wolf3d/WL_ACT2.C:SpawnBoss()`. Speed for Hitler chase (1024 shown) needs verification. Alert sound `HITLERHASND` used for both phases (verify against source).

### 12c. Gretel Grosse (Episode 4)

Gretel uses a chaingun identical to Hans but with different sprites and hit points.

```xml
<Actor Number="TODO" Name="Gretel" Page="TODO" />

<Actor Name="Gretel" HP="850,950,1050,1200" Reaction="1"
       AlertDigiSound="GUTENTAGSND"
       Stand="s_gretelstand" Chase="s_gretelchase1"
       Attack="s_gretelshoot1" Death="s_greteldie1" Ambush="true" />
<State Name="s_gretelstand"   Shape="SPR_GRETEL_W1"     Tics="0"  Think="T_Stand" Speed="0"   Next="s_gretelstand" />
<State Name="s_gretelchase1"  Shape="SPR_GRETEL_W1"     Tics="10" Think="T_Chase" Speed="512" Next="s_gretelchase1s" />
<State Name="s_gretelchase1s" Shape="SPR_GRETEL_W1"     Tics="3"                  Speed="512" Next="s_gretelchase2" />
<State Name="s_gretelchase2"  Shape="SPR_GRETEL_W2"     Tics="8"  Think="T_Chase" Speed="512" Next="s_gretelchase3" />
<State Name="s_gretelchase3"  Shape="SPR_GRETEL_W3"     Tics="10" Think="T_Chase" Speed="512" Next="s_gretelchase3s" />
<State Name="s_gretelchase3s" Shape="SPR_GRETEL_W3"     Tics="3"                  Speed="512" Next="s_gretelchase4" />
<State Name="s_gretelchase4"  Shape="SPR_GRETEL_W4"     Tics="8"  Think="T_Chase" Speed="512" Next="s_gretelchase1" />
<State Name="s_greteldie1"    Shape="SPR_GRETEL_DIE1"   Tics="15" Speed="0"       Action="A_DeathScream" Alive="false" Next="s_greteldie2" />
<State Name="s_greteldie2"    Shape="SPR_GRETEL_DIE2"   Tics="15" Speed="0"       Alive="false" Mark="false" Next="s_greteldie3" />
<State Name="s_greteldie3"    Shape="SPR_GRETEL_DIE3"   Tics="15" Speed="0"       Alive="false" Mark="false" Next="s_greteldie4" />
<State Name="s_greteldie4"    Shape="SPR_GRETEL_DEAD"   Tics="0"  Speed="0"       Action="A_StartDeathCam" Next="s_greteldie4" Alive="false" Mark="false" />
<State Name="s_gretelshoot1"  Shape="SPR_GRETEL_SHOOT1" Tics="30" Speed="0"       Next="s_gretelshoot2" />
<State Name="s_gretelshoot2"  Shape="SPR_GRETEL_SHOOT2" Tics="10" Speed="0"       Action="T_Shoot" Next="s_gretelshoot3" />
<State Name="s_gretelshoot3"  Shape="SPR_GRETEL_SHOOT3" Tics="10" Speed="0"       Action="T_Shoot" Next="s_gretelshoot4" />
<State Name="s_gretelshoot4"  Shape="SPR_GRETEL_SHOOT2" Tics="10" Speed="0"       Action="T_Shoot" Next="s_gretelshoot5" />
<State Name="s_gretelshoot5"  Shape="SPR_GRETEL_SHOOT3" Tics="10" Speed="0"       Action="T_Shoot" Next="s_gretelshoot6" />
<State Name="s_gretelshoot6"  Shape="SPR_GRETEL_SHOOT2" Tics="10" Speed="0"       Action="T_Shoot" Next="s_gretelshoot7" />
<State Name="s_gretelshoot7"  Shape="SPR_GRETEL_SHOOT3" Tics="10" Speed="0"       Action="T_Shoot" Next="s_gretelshoot8" />
<State Name="s_gretelshoot8"  Shape="SPR_GRETEL_SHOOT1" Tics="10" Speed="0"       Next="s_gretelchase1" />
```

### 12d. Otto Giftmacher (Episode 5)

Giftmacher throws one hypodermic needle per attack cycle (same projectile as Schabbs). Only 2 shoot states.

```xml
<Actor Number="TODO" Name="Giftmacher" Page="TODO" />

<Actor Name="Giftmacher" HP="850,950,1050,1200" Reaction="1"
       AlertDigiSound="TODO"
       Stand="s_giftstand" Chase="s_giftchase1"
       Attack="s_giftshoot1" Death="s_giftdie1" Ambush="true" />
<State Name="s_giftstand"   Shape="SPR_GIFT_W1"   Tics="0"  Think="T_Stand" Speed="0"   Next="s_giftstand" />
<State Name="s_giftchase1"  Shape="SPR_GIFT_W1"   Tics="10" Think="T_Gift"  Speed="512" Next="s_giftchase1s" />
<State Name="s_giftchase1s" Shape="SPR_GIFT_W1"   Tics="3"                  Speed="512" Next="s_giftchase2" />
<State Name="s_giftchase2"  Shape="SPR_GIFT_W2"   Tics="8"  Think="T_Gift"  Speed="512" Next="s_giftchase3" />
<State Name="s_giftchase3"  Shape="SPR_GIFT_W3"   Tics="10" Think="T_Gift"  Speed="512" Next="s_giftchase3s" />
<State Name="s_giftchase3s" Shape="SPR_GIFT_W3"   Tics="3"                  Speed="512" Next="s_giftchase4" />
<State Name="s_giftchase4"  Shape="SPR_GIFT_W4"   Tics="8"  Think="T_Gift"  Speed="512" Next="s_giftchase1" />
<!-- DeathCam replay state — A_StartDeathCam transitions into this for giftobj -->
<State Name="s_giftdeathcam" Shape="SPR_GIFT_W1"  Tics="1"  Speed="0"       Next="s_giftdie1" />
<State Name="s_giftdie1"    Shape="SPR_GIFT_W1"   Tics="1"  Speed="0" Action="A_DeathScream" Alive="false" Next="s_giftdie2" />
<State Name="s_giftdie2"    Shape="SPR_GIFT_W1"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_giftdie3" />
<State Name="s_giftdie3"    Shape="SPR_GIFT_DIE1" Tics="10" Speed="0" Alive="false" Mark="false" Next="s_giftdie4" />
<State Name="s_giftdie4"    Shape="SPR_GIFT_DIE2" Tics="10" Speed="0" Alive="false" Mark="false" Next="s_giftdie5" />
<State Name="s_giftdie5"    Shape="SPR_GIFT_DIE3" Tics="10" Speed="0" Alive="false" Mark="false" Next="s_giftdie6" />
<State Name="s_giftdie6"    Shape="SPR_GIFT_DEAD" Tics="20" Speed="0" Action="A_StartDeathCam" Next="s_giftdie6" Alive="false" Mark="false" />
<!-- Only 2 shoot states; action is T_GiftThrow (same function name as Fat Face) -->
<State Name="s_giftshoot1"  Shape="SPR_GIFT_SHOOT1" Tics="30" Speed="0" Next="s_giftshoot2" />
<State Name="s_giftshoot2"  Shape="SPR_GIFT_SHOOT2" Tics="10" Speed="0" Action="T_GiftThrow" Next="s_giftchase1" />
```

**TODO:** Verify alert sound against `wolf3d/WL_ACT2.C`. Note `s_needle1`–`s_needle4` are shared with Schabbs (already declared in 12a).

### 12e. General Fettgesicht (Episode 6)

Fat Face has a mixed attack: shoot2 throws a projectile (`T_GiftThrow`), then shoots3–6 are hitscan (`T_Shoot`). 6 shoot states total, alternating SHOOT3/SHOOT4 sprites for the hitscan burst.

```xml
<Actor Number="TODO" Name="FatFace" Page="TODO" />

<Actor Name="FatFace" HP="850,950,1050,1200" Reaction="1"
       AlertDigiSound="TODO"
       Stand="s_fatstand" Chase="s_fatchase1"
       Attack="s_fatshoot1" Death="s_fatdie1" Ambush="true" />
<State Name="s_fatstand"   Shape="SPR_FAT_W1"   Tics="0"  Think="T_Stand" Speed="0"   Next="s_fatstand" />
<State Name="s_fatchase1"  Shape="SPR_FAT_W1"   Tics="10" Think="T_Fat"   Speed="512" Next="s_fatchase1s" />
<State Name="s_fatchase1s" Shape="SPR_FAT_W1"   Tics="3"                  Speed="512" Next="s_fatchase2" />
<State Name="s_fatchase2"  Shape="SPR_FAT_W2"   Tics="8"  Think="T_Fat"   Speed="512" Next="s_fatchase3" />
<State Name="s_fatchase3"  Shape="SPR_FAT_W3"   Tics="10" Think="T_Fat"   Speed="512" Next="s_fatchase3s" />
<State Name="s_fatchase3s" Shape="SPR_FAT_W3"   Tics="3"                  Speed="512" Next="s_fatchase4" />
<State Name="s_fatchase4"  Shape="SPR_FAT_W4"   Tics="8"  Think="T_Fat"   Speed="512" Next="s_fatchase1" />
<!-- DeathCam replay state — A_StartDeathCam transitions into this for fatobj -->
<State Name="s_fatdeathcam" Shape="SPR_FAT_W1"  Tics="1"  Speed="0"       Next="s_fatdie1" />
<State Name="s_fatdie1"    Shape="SPR_FAT_W1"   Tics="1"  Speed="0" Action="A_DeathScream" Alive="false" Next="s_fatdie2" />
<State Name="s_fatdie2"    Shape="SPR_FAT_W1"   Tics="10" Speed="0" Alive="false" Mark="false" Next="s_fatdie3" />
<State Name="s_fatdie3"    Shape="SPR_FAT_DIE1" Tics="10" Speed="0" Alive="false" Mark="false" Next="s_fatdie4" />
<State Name="s_fatdie4"    Shape="SPR_FAT_DIE2" Tics="10" Speed="0" Alive="false" Mark="false" Next="s_fatdie5" />
<State Name="s_fatdie5"    Shape="SPR_FAT_DIE3" Tics="10" Speed="0" Alive="false" Mark="false" Next="s_fatdie6" />
<State Name="s_fatdie6"    Shape="SPR_FAT_DEAD" Tics="20" Speed="0" Action="A_StartDeathCam" Next="s_fatdie6" Alive="false" Mark="false" />
<!-- 6 shoot states: shoot2=projectile (T_GiftThrow), shoot3-6=hitscan (T_Shoot) -->
<State Name="s_fatshoot1"  Shape="SPR_FAT_SHOOT1" Tics="30" Speed="0" Next="s_fatshoot2" />
<State Name="s_fatshoot2"  Shape="SPR_FAT_SHOOT2" Tics="10" Speed="0" Action="T_GiftThrow" Next="s_fatshoot3" />
<State Name="s_fatshoot3"  Shape="SPR_FAT_SHOOT3" Tics="10" Speed="0" Action="T_Shoot" Next="s_fatshoot4" />
<State Name="s_fatshoot4"  Shape="SPR_FAT_SHOOT4" Tics="10" Speed="0" Action="T_Shoot" Next="s_fatshoot5" />
<State Name="s_fatshoot5"  Shape="SPR_FAT_SHOOT3" Tics="10" Speed="0" Action="T_Shoot" Next="s_fatshoot6" />
<State Name="s_fatshoot6"  Shape="SPR_FAT_SHOOT4" Tics="10" Speed="0" Action="T_Shoot" Next="s_fatchase1" />
```

**TODO:** Verify HP and alert sound against `wolf3d/WL_ACT2.C`.

---

## 13. Simulator — Lua Script Changes

All boss action functions live in `src/BenMcLean.Wolf3D.Simulator/Lua/DefaultScripts/Actors/`.

### 13a. Extend `A_StartDeathCam.lua`

The existing file only handles Hans. Add cases for all WL6 bosses (matching the `switch(ob->obclass)` in `wolf3d/WL_ACT2.C`):

```lua
-- WL_ACT2.C:A_StartDeathCam
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
```

### 13b. New `A_HitlerMorph.lua`

Create `src/BenMcLean.Wolf3D.Simulator/Lua/DefaultScripts/Actors/A_HitlerMorph.lua`. This fires on `s_mechadie3` and spawns Real Hitler at the mech's current tile position (`wolf3d/WL_ACT2.C:A_HitlerMorph`). `GetTileX()`, `GetTileY()`, `GetFacing()`, and `SpawnActor()` are all available in actor script context (`ActorScriptContext.cs` / `ActionScriptContext.cs`):

```lua
-- WL_ACT2.C:A_HitlerMorph - spawns Real Hitler at Mecha-Hitler's position
-- Called from s_mechadie3; the mech continues to s_mechadie4 (static dead state)
SpawnActor("Hitler", GetTileX(), GetTileY(), GetFacing())
```

---

## Summary Checklist

| # | Item | Data source | Status |
|---|---|---|---|
| 1 | Game root attributes | — | Ready to do |
| 2 | VgaGraph file refs | — | Ready to do |
| 3 | VgaGraph Pics block | `GFXV_WL6.H` | **Data complete — ready to do** |
| 4 | TextChunks block | `GFXV_WL6.H` | **Data complete — ready to do** |
| 5 | Main menu: remove "Read This!" | — | Ready to do |
| 6 | Episodes menu: unlock 2–6 | — | Ready to do |
| 7 | Victory menu: per-episode EndArt | — | Ready to do |
| 8 | DeathCam menus (5 new pairs) | `WL_ACT2.C` | Ready to do (pattern known) |
| 9 | Audio file refs + FIRESND rename | — | Ready to do |
| 10 | Maps: 50 new entries + Par/Song | `WL_GAME.C:levelinfo[]` | **TODO: look up Par/Song data** |
| 11 | VSwap file ref | — | Ready to do |
| 12 | Boss actors + states (5 bosses) | `WL_ACT2.C`, `WL_DEF.H` | Ready (verify HP/Speed/sounds) |
| 13 | `A_MechaDeathSpawnHitler` in C# | `WL_ACT2.C` | Requires code change |

**Largest remaining unknowns:**
- Map Par times and Song assignments (item 10) — read `wolf3d/WL_GAME.C:levelinfo[]`
- Boss spawn object `Number` and `Page` values (items 12a–e) — count VSWAP pages
- Projectile speeds for Schabbs hypos (item 12a) — read `wolf3d/WL_ACT2.C`
- Mecha-Hitler step sound implementation (item 12b) — read `WL_ACT2.C:T_MechaSound`
