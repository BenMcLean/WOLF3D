if GetValue("Ammo") < GetMax("Ammo") then
	AddValue("Ammo", 8)
	PlaySound("GETAMMOSND")
	FlashScreen(0xFFF800)
	return true
end
return false
