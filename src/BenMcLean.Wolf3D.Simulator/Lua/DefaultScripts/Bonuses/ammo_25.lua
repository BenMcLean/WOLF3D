if GetValue("Ammo") < GetMax("Ammo") then
	AddValue("Ammo", 25)
	PlaySound("GETAMMOBOXSND")
	FlashScreen(0xFFF800)
	return true
end
return false
