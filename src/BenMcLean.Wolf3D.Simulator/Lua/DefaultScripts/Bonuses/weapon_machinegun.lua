local isNew = not Has("Weapon2")
SetValue("Weapon2", 1)
AddValue("bullets", 6)
if isNew then SwitchToWeapon("machinegun") end
PlaySound("GETMACHINESND")
FlashScreen(0xFFF800)
return true
