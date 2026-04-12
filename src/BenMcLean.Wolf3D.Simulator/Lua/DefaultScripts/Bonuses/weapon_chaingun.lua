local isNew = not Has("Weapon3")
SetValue("Weapon3", 1)
AddValue("Ammo", 6)
if isNew then SwitchToWeapon("chaingun") end
SetValue("FaceGrinTics", 4)
PlayAdLibSound("GETGATLINGSND")
FlashScreen(0xFFF800)
return true
