#!/bin/bash
# Convert 6-bit VGA palette binary file (like GAMEPAL.BIN) to hex XML format
# Usage: ./convert_vga_pal_to_hex.sh <input.bin> > output.xml
#
# Example: ./convert_vga_pal_to_hex.sh wolf3d/STATIC/WOLF3D/GAMEPAL.BIN > palette.xml
#
# Reads a 768-byte binary file (256 colors × 3 bytes RGB)
# VGA palette values are 6-bit (0-63), converted to 8-bit (0-255) by left-shifting 2 bits
# This matches the VGA DAC hardware behavior where palette registers were only 6 bits per channel.

if [ "$#" -ne 1 ]; then
	echo "Usage: $0 <input.bin>" >&2
	echo "" >&2
	echo "Example: $0 wolf3d/STATIC/WOLF3D/GAMEPAL.BIN > palette.xml" >&2
	echo "" >&2
	echo "Converts 6-bit VGA binary palette (768 bytes) to hex XML format." >&2
	echo "VGA palette values are 6-bit (0-63), converted to 8-bit (0-255) by left-shifting 2 bits." >&2
	echo "Output goes to stdout. Status messages go to stderr." >&2
	exit 1
fi

INPUT_FILE="$1"

if [ ! -f "$INPUT_FILE" ]; then
	echo "Error: Input file '$INPUT_FILE' not found!" >&2
	exit 1
fi

# Verify file size is exactly 768 bytes
if command -v stat >/dev/null 2>&1; then
	FILE_SIZE=$(stat -c%s "$INPUT_FILE" 2>/dev/null || stat -f%z "$INPUT_FILE" 2>/dev/null)
	if [ "$FILE_SIZE" != "768" ]; then
		echo "Error: Input file must be exactly 768 bytes (256 colors × 3 bytes RGB)" >&2
		echo "Got: $FILE_SIZE bytes" >&2
		exit 1
	fi
fi

# Extract and convert palette - output to stdout
printf "<Palette>\n"

# Read binary file, convert 6-bit values (0-63) to 8-bit (0-255) by left-shifting 2 bits
# od outputs values across multiple lines, so we collect all values then process in groups of 3
od -A n -t u1 -v "$INPUT_FILE" | \
	awk '
	{
		for (i=1; i<=NF; i++) {
			values[count++] = $i
		}
	}
	END {
		for (i=0; i<count; i+=3) {
			if (i+2 < count) {
				r = lshift(values[i], 2)
				g = lshift(values[i+1], 2)
				b = lshift(values[i+2], 2)
				printf "\t<Color Hex=\"#%02X%02X%02X\"/>\n", r, g, b
			}
		}
	}'

printf "</Palette>\n"
