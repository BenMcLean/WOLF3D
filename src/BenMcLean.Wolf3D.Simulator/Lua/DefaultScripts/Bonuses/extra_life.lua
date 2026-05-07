AddValue("Lives", 1)
SetValue("Health", GetMax("Health"))
AddValue("bullets", 25)
PlaySound(ResolveSound("BONUS1UPSND", "D_EXTRASND"))
FlashScreen(0xFFF800)
return true
