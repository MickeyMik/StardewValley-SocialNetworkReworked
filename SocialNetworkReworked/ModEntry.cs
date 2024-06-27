using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GenericModConfigMenu;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using SocialNetworkReworked.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Characters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace SocialNetworkReworked
{
	/// <summary>		The mod entry point.													</summary>
	internal sealed class ModEntry : Mod
	{
		/*********
		 ** Overview
		 *********
		 *		On Game Launched:
		 *				Initialise GMCM Menu, if mod is installed.
		 *********
		 *		On Start of Day (waking up in-game):
		 *				Refresh villager list (with current heart level and relationships).
		 *********
		 *		On End of Day (going to sleep in-game):
		 *				If an NPC gained or lost a heart by the end of the day,
		 *					the NPCs in their social network gain or lose a Social Network Bonus (default +/-50 points).
		 *********
		 *		Remark:
		 *				The check is at the end of the day for two reasons:
		 *					1. Code Optimisation, only has to check once, therefore looking more neat and is more stable in-game.
		 *					2. I imagine that it would take a while for friends and family to learn about the change in friendship.
		 *********/


		/// <summary>	Metadata for the villagers, their stored heart levels,
		///					and their relationships.											</summary>
		private IDictionary<string, VillagerNetwork> Villagers;
		private ModConfig Config;


		/*********
		 ** Public methods
		 *********/
		/// <summary>	The mod entry point, called after the mod is first loaded.				</summary>
		/// <param name="helper">	Provides simplified APIs for writing mods.					</param>
		public override void Entry(IModHelper helper)
		{
			helper.Events.GameLoop.GameLaunched += OnGameLaunched;
			helper.Events.GameLoop.DayStarted += OnDayStarted;
			helper.Events.GameLoop.DayEnding += OnDayEnding;
		}


		/*********
		 ** Private methods
		 *********/
		/// <summary>	Raised after the game is launched, right before the first update tick.
		///					This happens once per game session (unrelated to loading saves).	</summary>
		/// <param name="sender">	The event sender.											</param>
		/// <param name="e">		The event data.												</param>
		private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
		{
			// Read JSON file or create one if it doesn't exist
			Config = this.Helper.Data.ReadJsonFile<ModConfig>("config.json") ?? new ModConfig();
			// Save JSON file locally in mod folder
			this.Helper.Data.WriteJsonFile("config.json", Config);

			// get Generic Mod Config Menu's API (if it's installed)
			var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
			if (configMenu is null)
				return;

			// register mod
			configMenu.Register(
				mod: this.ModManifest,
				reset: () => this.Config = new ModConfig(),
				save: () => this.Helper.WriteConfig(this.Config)
			);

			// config menu options
			configMenu.AddSectionTitle(
				mod: this.ModManifest,
				text: () => "Mod Settings",
				tooltip: () => "A recreation of Cecidelus' mod of the same name, 'SocialNetwork'."
			);
			configMenu.AddParagraph(
				mod: this.ModManifest,
				text: () => "When a villager gains or loses a heart, their close friends and family will gain or lose friendship points equal to the Social Network Bonus." +
							"1 heart is equal to 250 friendship points. Hover over the settings for a description."
			);
			configMenu.AddBoolOption(
				mod: this.ModManifest,
				name: () => "Show Network Pop-up Message",
				tooltip: () => "A pop-up at the end of day showing which villagers got points due to heart change. (Default on)",
				getValue: () => this.Config.NetworkMessage,
				setValue: value => this.Config.NetworkMessage = value
			);
			configMenu.AddNumberOption(
				mod: this.ModManifest,
				name: () => "Social Network Bonus",
				tooltip: () => "The amount of friendship points given to villagers' friends and family when they lose or gain a heart. 250 points = 1 heart. (Default 50 points)",
				getValue: () => this.Config.SocialNetworkBonus,
				setValue: value => this.Config.SocialNetworkBonus = value
			);
		}

		/// <summary>	Raised after the game begins a new day
		///					(including when the player loads a save).							</summary>
		/// <param name="sender">	The event sender.											</param>
		/// <param name="e">		The event data.												</param>
		private void OnDayStarted(object? sender, DayStartedEventArgs e) =>
			// Refresh villager network list, including relationships and heart level.
			Villagers = GetVillagersAndConnections();

		/// <summary>	Raised before the game ends the current day.
		///					This happens before it starts setting up the next day
		///					and before Saving.													</summary>
		/// <param name="sender">	The event sender.											</param>
		/// <param name="e">		The event data.												</param>
		private void OnDayEnding(object? sender, DayEndingEventArgs e)
		{
			// Read each NPC's heart level; if it has changed from the beginning of the day, trigger the social network bonus
			foreach (VillagerNetwork villager in Villagers.Values)
			{
				// if heart level has increased, give social network bonus to related npcs
				if (villager.HeartLevel < CurrentVillagerHearts(villager.Name))
					TriggerSocialNetworkBonus(villager, Config.SocialNetworkBonus);
				// if heart level has decreased, take away social network bonus from related npcs
				else if (villager.HeartLevel > CurrentVillagerHearts(villager.Name))
					TriggerSocialNetworkBonus(villager, -Config.SocialNetworkBonus);
			}
		}


		/*********
		** Utilised methods
		*********/
		/// <summary>	gets the current heart level for the named npc							</summary>
		/// <param name="name">		Name of NPC													</param>
		private static int CurrentVillagerHearts(string name) =>
			Game1.player.getFriendshipHeartLevelForNPC(name);


		/// <summary>	Change friendship points with NPCs related to villager.					</summary>
		/// <param name="villager">	The original villager.										</param>
		/// <param name="points">	The number of points to add or remove 
		///								from NPCs in their social network.						</param>
		private void TriggerSocialNetworkBonus(VillagerNetwork villager, int points)
		{
			foreach (string relatedVillager in villager.Relationships)
			{
				Game1.player.changeFriendship(points, Game1.getCharacterFromName(relatedVillager));
				if (points < 0)
					ShowNotification($"{relatedVillager}: {points} friendship");
				else if (points > 0)
					ShowNotification($"{relatedVillager}: +{points} friendship");

			}
		}

		/// <summary>	Show notification in-game and info log it on the SMAPI console.
		///					No icon, lasting 5s.												</summary>
		/// <param name="message">	Message to display.											</param>
		private void ShowNotification(string message)
		{
			if (Config.NetworkMessage)
				Game1.addHUDMessage(new HUDMessage(message) { noIcon = true, timeLeft = 5250f });
			this.Monitor.Log(message, LogLevel.Info);
		}


		/// <summary>	Get all available characters, their heart level, 
		///					and their relationships.											</summary>
		private IDictionary<string, VillagerNetwork> GetVillagersAndConnections()
		{
			Dictionary<string, VillagerNetwork> villagers = new();

			// Create and store current NPCs names and heart levels
			// (unoptimised use; should not have to refresh the whole dictionary just to update heart levels...)
			// (doing it in case villagers need to be refreshed each day, also looks simpler and neater code-wise)
			Utility.ForEachVillager(delegate (NPC npc)
			{
				// create VillagerNetwork instance for each npc
				VillagerNetwork newVillager = new(npc.Name, CurrentVillagerHearts(npc.Name), npc.GetData().FriendsAndFamily);
				if (villagers.TryAdd(npc.Name, newVillager))
					return true;
				else
					return false;
			});

			return villagers;
		}
	}
}