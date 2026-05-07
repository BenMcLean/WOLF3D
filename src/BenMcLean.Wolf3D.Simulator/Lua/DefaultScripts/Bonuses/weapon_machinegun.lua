local isNew = not Has("Weapon2")
SetValue("Weapon2", 1)
AddValue("bullets", 6)
if isNew then SwitchToWeapon("machinegun") end
PlaySound(ResolveSound("GETMACHINESND", "D_BONUSSND"))
FlashScreen(0xFFF800)
return true
