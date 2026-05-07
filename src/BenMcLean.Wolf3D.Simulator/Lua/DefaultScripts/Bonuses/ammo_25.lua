if GetValue("bullets") < GetMax("bullets") then
	AddValue("bullets", 25)
	PlaySound(ResolveSound("GETAMMOBOXSND", "D_BONUSSND"))
	FlashScreen(0xFFF800)
	return true
end
return false
