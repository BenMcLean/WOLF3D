local isNew = not Has("Weapon2")
SetValue("Weapon2", 1)
AddValue("Ammo", 6)
if isNew then SwitchToWeapon("machinegun") end
PlayAdLibSound("GETMACHINESND")
FlashScreen(0xFFF800)
return true
