using System;
using System.Linq;
using BenMcLean.Wolf3D.Assets.Menu;

namespace BenMcLean.Wolf3D.Shared.Menu;

/// <summary>
/// Builds a procedural MenuCollection for the game selection screen.
/// Merges embedded official XML with optional user XML overrides/additions from the games/
/// directory, then creates a selectable paged list using the shareware (WL1) visual assets
/// already loaded via SharedAssetManager.
/// "Have it list games." --Matthew Broderick as David Lightman in WarGames. (1983)
/// </summary>
public static class GameSelectionMenuFactory
{
	public const int PerPage = 10;
	/// <summary>
	/// Builds a MenuCollection containing paginated "_GameSelectN" menus that list
	/// all playable game definition files available for the specified directory.
	/// Each menu item calls SelectGame(path) when activated.
	/// WL1 is sorted first, then alphabetically by filename.
	/// </summary>
	/// <param name="gamesDirectory">Absolute path to the games/ directory</param>
	/// <returns>A MenuCollection ready to pass to MenuRoom as MenuCollectionOverride</returns>
	public static MenuCollection Build(string gamesDirectory)
	{
		GameCatalog.GameDefinition[] games = [.. GameCatalog.GetAvailableGames(gamesDirectory)];
		int pages = games.Length == 0 ? 1 : (games.Length - 1) / PerPage + 1;
		MenuCollection collection = new()
		{
			DefaultFont = "SMALL",
			DefaultSpacing = 13,
			DefaultTextColor = 0x17,          // WL_MENU.H:TEXTCOLOR
			DefaultHighlight = 0x13,          // WL_MENU.H:HIGHLIGHT
			DefaultBordColor = 0x29,        // WL_MENU.H:BORDCOLOR
			DefaultBkgdColor = 0x2d, // WL_MENU.H:BKGDCOLOR
			DefaultDeactive = 0x2b,           // WL_MENU.H:DEACTIVE
			DefaultBord2Color = 0x23,       // WL_MENU.H:BORD2COLOR
			DefaultCursorPic = "C_CURSOR1PIC",
			DefaultCursorMoveSound = "MOVEGUN2SND",
		};
		collection.EndStrings.Add("Are you sure you want\nto quit?");
		for (int page = 0; page < pages; page++)
		{
			int prevPage = (page - 1 + pages) % pages,
				nextPage = (page + 1) % pages;
			MenuDefinition menu = new()
			{
				Name = "_GameSelect" + page,
				Music = "GAMESELECT_MUS",
				BordColor = 0x29,
				TextColor = 0x17,
				Highlight = 0x13,
				Font = "SMALL",
				CursorPic = "C_CURSOR1PIC",
				CursorMoveSound = "MOVEGUN2SND",
				X = 8,
				Y = 22,
				Indent = 24,
				Spacing = 13,
				Texts =
				[
					new TextDefinition
					{
						Content = "Which game to play?",
						X = "Center",
						Y = "3",
						Color = 71, // READHCOLOR - yellow
					},
				],
				Boxes =
				[
					new MenuBoxDefinition
					{
						X = 2,
						Y = 19,
						W = 316,
						H = 168,
						BkgdColor = 45, // PixelRect Color
						Deactive = 44,        // PixelRect BordColor
						Bord2Color = 35,    // PixelRect Bord2Color
					},
				],
			};
			if (pages > 1)
			{
				menu.Texts.Add(new TextDefinition
				{
					Content = "pg " + (page + 1) + " of " + pages,
					X = "220",
					Y = "188",
					Color = 0x17,
				});
				menu.Items.Add(new MenuItemDefinition
				{
					Text = "\u00ab Prev",
					Script = $"NavigateToMenu(\"_GameSelect{prevPage}\")",
				});
			}
			if (games.Length == 0)
				menu.Items.Add(new MenuItemDefinition
				{
					Text = "No games found",
					Script = "",
				});
			else
			{
				int startIdx = page * PerPage,
					endIdx = Math.Min(startIdx + PerPage, games.Length);
				for (int i = startIdx; i < endIdx; i++)
				{
					string name = games[i].DisplayName,
						normalizedPath = games[i].XmlPath.Replace('\\', '/');
					menu.Items.Add(new MenuItemDefinition
					{
						Text = name,
						Script = $"SelectGame(\"{normalizedPath}\")",
					});
				}
			}
			if (pages > 1)
				menu.Items.Add(new MenuItemDefinition
				{
					Text = "Next \u00bb",
					Script = $"NavigateToMenu(\"_GameSelect{nextPage}\")",
				});
			collection.AddMenu(menu);
		}
		return collection;
	}
}
