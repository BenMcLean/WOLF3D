using System;
using System.Collections.Generic;
using BenMcLean.Wolf3D.Assets.Graphics;

namespace BenMcLean.Wolf3D.Assets.Menu;

/// <summary>
/// C# port of WL_TEXT.C PageLayout — parses Wolf3D text chunk format into
/// per-page layout data ready for rendering.
///
/// The Wolf3D text format uses caret commands embedded in ASCII text:
///   ^P[newline]              — start of page (must come first)
///   ^E[newline]              — end of file
///   ^C&lt;HH&gt;                  — change color (two hex digits, VGA palette index)
///   ^G&lt;y&gt;,&lt;x&gt;,&lt;n&gt;[newline]  — draw graphic at (x,y) with absolute chunk index n
///   ^L&lt;y&gt;,&lt;x&gt;[newline]      — locate cursor (y in pixels, x in pixels)
///   ^T&lt;y&gt;,&lt;x&gt;,&lt;n&gt;,&lt;t&gt;[nl]  — timed graphic (like ^G, delay t ignored)
///   ^B&lt;y&gt;,&lt;x&gt;,&lt;w&gt;,&lt;h&gt;[nl]  — clear bar (ignored: background is rendered by renderer)
///   ^&gt;                       — center cursor (px = 160)
///   ;...                     — comment (rest of line ignored)
///   TAB (0x09)               — advance px to next 8-pixel boundary
///   newline (0x0A)           — move to next text row
///
/// All other characters &lt;= 32 are ignored.
/// Characters &gt; 32 form words, rendered with word-wrap against per-row margins.
///
/// WL_TEXT.C constants (VGA version, post-0x920416):
///   TOPMARGIN=16, BOTTOMMARGIN=32, LEFTMARGIN=16, RIGHTMARGIN=16
///   FONTHEIGHT=10, TEXTROWS=15, SPACEWIDTH=7
///   SCREENPIXWIDTH=320, SCREENMID=160, PICMARGIN=8, BACKCOLOR=0x11
/// </summary>
public static class ArticleLayoutEngine
{
	// WL_TEXT.C rendering constants (VGA version)
	public const int TopMargin = 16;
	public const int BottomMargin = 32;
	public const int LeftMargin = 16;
	public const int RightMargin = 16;
	public const int FontHeight = 10;
	public const int TextRows = (200 - TopMargin - BottomMargin) / FontHeight; // 15
	public const int SpaceWidth = 7;
	public const int ScreenPixWidth = 320;
	public const int ScreenMid = 160;
	public const int PicMargin = 8;
	// WL_TEXT.C: BACKCOLOR = 0x11 (VGA palette index 17, dark blue)
	public const byte BackColor = 0x11;
	// WL_TEXT.C: page number drawn at py=183, px=213, fontcolor=0x4F (79)
	public const int PageNumX = 213;
	public const int PageNumY = 183;
	public const byte PageNumColor = 0x4F;

	/// <summary>
	/// Parse and lay out all pages of a Wolf3D text article.
	/// </summary>
	/// <param name="rawText">
	/// Raw ASCII text from HELPART.WL1 or VGAGRAPH text chunk.
	/// </param>
	/// <param name="font">
	/// The Wolf3D small font (ChunkFontsByName["SMALL"], index 0).
	/// Used to measure word widths for word wrap.
	/// </param>
	/// <param name="picNameResolver">
	/// Resolves an absolute VGAGRAPH chunk index to a pic name.
	/// Returns null for unknown indices.
	/// WL_TEXT.C: ^G command picnum → VWB_DrawPic(picnum)
	/// </param>
	/// <param name="picSizeResolver">
	/// Resolves an absolute VGAGRAPH chunk index to (width, height) in pixels.
	/// Used to adjust per-row margins so text flows around graphics.
	/// WL_TEXT.C: pictable[picnum-STARTPICS].width/height
	/// </param>
	/// <returns>One ArticlePageLayout per page, in order.</returns>
	public static ArticlePageLayout[] Layout(
		string rawText,
		Font font,
		Func<int, string> picNameResolver,
		Func<int, (int width, int height)?> picSizeResolver = null,
		byte backColor = BackColor)
	{
		if (string.IsNullOrEmpty(rawText))
			return [];
		int totalPages = CountPages(rawText);
		if (totalPages == 0)
			return [];
		List<ArticlePageLayout> pages = [];
		int pos = 0;
		for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
		{
			ArticlePageLayout page = LayoutPage(rawText, ref pos, font, picNameResolver, picSizeResolver, backColor, pageIndex + 1, totalPages);
			pages.Add(page);
		}
		return [.. pages];
	}

	/// <summary>
	/// Pre-scan: count ^P markers to determine total page count.
	/// WL_TEXT.C: CacheLayoutGraphics() counts numpages.
	/// </summary>
	private static int CountPages(string text)
	{
		int count = 0;
		for (int i = 0; i < text.Length - 1; i++)
			if (text[i] == '^' && char.ToUpper(text[i + 1]) == 'P')
				count++;
		return count;
	}

	/// <summary>
	/// Lay out one page starting at <paramref name="pos"/> in the text stream.
	/// Advances <paramref name="pos"/> past the page's ^P/^E terminator.
	/// WL_TEXT.C: PageLayout(true) — one call per page.
	/// </summary>
	private static ArticlePageLayout LayoutPage(
		string text,
		ref int pos,
		Font font,
		Func<int, string> picNameResolver,
		Func<int, (int width, int height)?> picSizeResolver,
		byte backColor,
		int pageNumber,
		int totalPages)
	{
		ArticlePageLayout page = new()
		{
			PageNumber = pageNumber,
			TotalPages = totalPages,
			BackColor = backColor,
		};

		// Per-row margin arrays (WL_TEXT.C: leftmargin[], rightmargin[])
		int[] leftMargin = new int[TextRows];
		int[] rightMargin = new int[TextRows];
		for (int i = 0; i < TextRows; i++)
		{
			leftMargin[i] = LeftMargin;
			rightMargin[i] = ScreenPixWidth - RightMargin;
		}

		int px = LeftMargin;
		int py = TopMargin;
		int rowOn = 0;
		bool layoutDone = false;
		byte fontColor = 0; // WL_TEXT.C: fontcolor initialized to 0 before PageLayout

		// Advance past whitespace before ^P
		while (pos < text.Length && text[pos] <= ' ')
			pos++;
		// Consume the opening ^P[newline] — WL_TEXT.C: "Text not headed with ^P"
		if (pos < text.Length - 1 && text[pos] == '^' && char.ToUpper(text[pos + 1]) == 'P')
		{
			pos += 2;
			while (pos < text.Length && text[pos] != '\n')
				pos++;
			if (pos < text.Length) pos++; // consume newline
		}

		// Main layout loop — WL_TEXT.C: PageLayout do { ch = *text; ... } while (!layoutdone)
		while (!layoutDone && pos < text.Length)
		{
			char ch = text[pos];

			if (ch == '^')
			{
				HandleCommand(
					text, ref pos,
					ref px, ref py, ref rowOn, ref layoutDone, ref fontColor,
					leftMargin, rightMargin,
					font, picNameResolver, picSizeResolver,
					page);
			}
			else if (ch == '\t') // TAB: align to next 8-pixel boundary
			{
				px = (px + 8) & ~7;
				pos++;
			}
			else if (ch <= ' ') // WL_TEXT.C: HandleCtrls
			{
				if (ch == '\n')
					NewLine(ref rowOn, ref px, ref py, ref layoutDone, leftMargin);
				pos++;
			}
			else // WL_TEXT.C: HandleWord
			{
				HandleWord(
					text, ref pos,
					ref px, ref py, ref rowOn, ref layoutDone,
					fontColor, leftMargin, rightMargin,
					font, page);
			}
		}

		// Page number run — WL_TEXT.C: if (shownumber) { py=183; px=213; fontcolor=0x4F; }
		page.TextRuns.Add(new ArticleTextRun
		{
			X = PageNumX,
			Y = PageNumY,
			Color = PageNumColor,
			Text = $"pg {pageNumber} of {totalPages}",
		});

		return page;
	}

	/// <summary>
	/// Process a ^ command at the current position.
	/// WL_TEXT.C: HandleCommand()
	/// </summary>
	private static void HandleCommand(
		string text, ref int pos,
		ref int px, ref int py, ref int rowOn, ref bool layoutDone, ref byte fontColor,
		int[] leftMargin, int[] rightMargin,
		Font font, Func<int, string> picNameResolver, Func<int, (int width, int height)?> picSizeResolver,
		ArticlePageLayout page)
	{
		pos++; // skip '^'
		if (pos >= text.Length)
			return;
		char cmd = char.ToUpper(text[pos]);
		pos++; // skip command char

		switch (cmd)
		{
		case 'P': // start of next page — stop this page
		case 'E': // end of file
			layoutDone = true;
			pos -= 2; // WL_TEXT.C: "back up to the '^'" so caller can find ^E/^P
			break;

		case 'C': // ^C<HH> change color (two VGA hex digits)
		{
			if (pos >= text.Length) break;
			int high = HexVal(text[pos++]);
			int low = pos < text.Length ? HexVal(text[pos++]) : 0;
			fontColor = (byte)(high * 16 + low);
			break;
		}

		case 'G': // ^G<y>,<x>,<picnum>[newline] draw graphic
		case 'T': // ^T<y>,<x>,<picnum>,<delay>[newline] timed draw (delay ignored)
		{
			int picy = ParseNumber(text, ref pos);
			int picx = ParseNumber(text, ref pos);
			int picnum = ParseNumber(text, ref pos);
			if (cmd == 'T')
				ParseNumber(text, ref pos); // discard delay
			RipToEOL(text, ref pos);

			string picName = picNameResolver?.Invoke(picnum);
			int alignedX = picx & ~7; // WL_TEXT.C: VWB_DrawPic(picx&~7, picy, picnum)
			page.Graphics.Add(new ArticleGraphicItem { X = alignedX, Y = picy, PicName = picName });

			// Adjust per-row margins so text flows around the graphic
			// WL_TEXT.C: HandleCommand case 'G' margin adjustment
			(int width, int height)? size = picSizeResolver?.Invoke(picnum);
			if (size.HasValue)
			{
				int picwidth = size.Value.width;
				int picheight = size.Value.height;
				// WL_TEXT.C: picmid = picx + picwidth/2; if (picmid > SCREENMID) new right margin else new left margin
				int picmid = picx + picwidth / 2;
				int margin = picmid > ScreenMid
					? picx - PicMargin             // image on right: shrink right margin
					: picx + picwidth + PicMargin; // image on left: shrink left margin
				int top = (picy - TopMargin) / FontHeight;
				if (top < 0) top = 0;
				int bottom = (picy + picheight - TopMargin) / FontHeight;
				if (bottom >= TextRows) bottom = TextRows - 1;
				for (int i = top; i <= bottom; i++)
				{
					if (picmid > ScreenMid)
						rightMargin[i] = margin;
					else
						leftMargin[i] = margin;
				}
				// WL_TEXT.C: if (px < leftmargin[rowon]) px = leftmargin[rowon];
				if (px < leftMargin[rowOn])
					px = leftMargin[rowOn];
			}
			break;
		}

		case 'L': // ^L<y>,<x>[newline] locate cursor (y first, then x)
		{
			int newPy = ParseNumber(text, ref pos);
			int newPx = ParseNumber(text, ref pos);
			RipToEOL(text, ref pos);
			rowOn = (newPy - TopMargin) / FontHeight;
			if (rowOn < 0) rowOn = 0;
			if (rowOn >= TextRows) rowOn = TextRows - 1;
			py = TopMargin + rowOn * FontHeight;
			px = newPx;
			break;
		}

		case 'B': // ^B<y>,<x>,<w>,<h>[newline] clear bar (no-op: renderer draws background)
			ParseNumber(text, ref pos);
			ParseNumber(text, ref pos);
			ParseNumber(text, ref pos);
			ParseNumber(text, ref pos);
			RipToEOL(text, ref pos);
			break;

		case '>': // ^> center cursor (px = SCREENMID)
			px = ScreenMid;
			break;

		case ';': // comment — skip to end of line
			RipToEOL(text, ref pos);
			break;

		default:
			break;
		}
	}

	/// <summary>
	/// Lay out one word (all chars > 32 until whitespace/control).
	/// WL_TEXT.C: HandleWord()
	/// </summary>
	private static void HandleWord(
		string text, ref int pos,
		ref int px, ref int py, ref int rowOn, ref bool layoutDone,
		byte fontColor, int[] leftMargin, int[] rightMargin,
		Font font, ArticlePageLayout page)
	{
		// Collect word characters (WL_TEXT.C: while (*text > 32))
		int wordStart = pos;
		while (pos < text.Length && text[pos] > ' ')
			pos++;
		if (pos == wordStart)
			return;
		string word = text[wordStart..pos];

		// Measure word width using font glyph widths (WL_TEXT.C: VW_MeasurePropString)
		int wwidth = MeasureString(word, font);

		// Word-wrap: advance to next line while word doesn't fit
		while (px + wwidth > rightMargin[rowOn])
		{
			NewLine(ref rowOn, ref px, ref py, ref layoutDone, leftMargin);
			if (layoutDone)
				return;
		}

		// Emit text run at current position
		page.TextRuns.Add(new ArticleTextRun
		{
			X = px,
			Y = py,
			Color = fontColor,
			Text = word,
		});
		px += wwidth;

		// Consume trailing spaces (WL_TEXT.C: while (*text == ' ') { px += SPACEWIDTH; text++; })
		while (pos < text.Length && text[pos] == ' ')
		{
			px += SpaceWidth;
			pos++;
		}
	}

	/// <summary>
	/// Advance to the next text row.
	/// WL_TEXT.C: NewLine()
	/// </summary>
	private static void NewLine(
		ref int rowOn, ref int px, ref int py, ref bool layoutDone, int[] leftMargin)
	{
		if (++rowOn == TextRows)
		{
			// WL_TEXT.C: overflowed page — skip until next ^P or ^E
			layoutDone = true;
			return;
		}
		py += FontHeight;
		px = leftMargin[rowOn];
	}

	/// <summary>
	/// Measure the pixel width of a string using proportional font glyph widths.
	/// WL_TEXT.C: VW_MeasurePropString()
	/// </summary>
	private static int MeasureString(string s, Font font)
	{
		if (font is null)
			return s.Length * 8; // fallback: 8px per char
		int width = 0;
		foreach (char c in s)
			if (c < font.Widths.Length)
				width += font.Widths[c];
		return width;
	}

	/// <summary>
	/// Parse a decimal integer from the text stream, stopping at any non-digit.
	/// WL_TEXT.C: ParseNumber()
	/// </summary>
	private static int ParseNumber(string text, ref int pos)
	{
		int num = 0;
		while (pos < text.Length)
		{
			char c = text[pos];
			if (c >= '0' && c <= '9')
			{
				num = num * 10 + (c - '0');
				pos++;
			}
			else
			{
				pos++; // skip the separator (comma or other non-digit)
				break;
			}
		}
		return num;
	}

	/// <summary>
	/// Advance position past the end of the current line (past the '\n').
	/// WL_TEXT.C: RipToEOL()
	/// </summary>
	private static void RipToEOL(string text, ref int pos)
	{
		while (pos < text.Length && text[pos] != '\n')
			pos++;
		if (pos < text.Length) pos++; // consume '\n'
	}

	/// <summary>
	/// Convert a single ASCII hex character to its integer value (0–15).
	/// Returns 0 for invalid characters.
	/// </summary>
	private static int HexVal(char c)
	{
		if (c >= '0' && c <= '9') return c - '0';
		c = char.ToUpper(c);
		if (c >= 'A' && c <= 'F') return c - 'A' + 10;
		return 0;
	}
}
