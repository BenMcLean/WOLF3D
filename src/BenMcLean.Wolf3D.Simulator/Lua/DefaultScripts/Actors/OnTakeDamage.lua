local amount = ...
if GetValue("Difficulty") == 0 then
	return BitShiftRight(amount, 2)
end

return amount
