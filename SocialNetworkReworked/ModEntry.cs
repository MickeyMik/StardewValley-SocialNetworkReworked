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
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
	{
		/// <summary>Metadata for the villagers, their stored heart levels, and their relationships.</summary>
		private IDictionary<string, VillagerNetwork> Villagers;
		private ModConfig Config;

		/*********
        ** Public methods
        *********/
		/// <summary>The mod entry point, called after the mod is first loaded.</summary>
		/// <param name="helper">Provides simplified APIs for writing mods.</param>
		public override void Entry(IModHelper helper)
		{
			helper.Events.GameLoop.GameLaunched += OnGameLaunched;
			helper.Events.GameLoop.DayStarted += OnDayStarted;
			helper.Events.GameLoop.DayEnding += OnDayEnding;
		}

		/*********
        ** Private methods
        *********/

		/*
		 * Initialise villager list (with current heart level and social network) at game launched.
         * 
         * If an NPC gained or lost a heart by the end of the day,
         *		the NPC in their social network gain or lose a Social Network Bonus (default 50 points).
         * 
         *		(The reason it is only at the end of the day
         *		 is because I imagine that the social network NPCs
         *		 would only learn about it after having had time to chat with eachother.)
         */

		/// <summary>	Raised after the game is launched, right before the first update tick.
		///				This happens once per game session (unrelated to loading saves).		</summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The event data.</param>
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
				tooltip: () => "A pop-up at the end of day showing which villagers got points due to heart change. (Default off)",
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

		/// <summary>Raised after the game begins a new day (including when the player loads a save).</summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The event data.</param>
		private void OnDayStarted(object? sender, DayStartedEventArgs e) =>
			// Refresh villager network list, including relationships and heart level.
			Villagers = GetVillagersAndConnections();

		/// <summary>Raised before the game ends the current day. This happens before it starts setting up the next day and before Saving.</summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The event data.</param>
		private void OnDayEnding(object? sender, DayEndingEventArgs e)
		{
			// read NPC's friendship value change; if the change reached a new bracket, trigger the social network bonus
			//	(including to people who the player hasn't met, which I think should be changed so that it is only with people that one has met,
			//		but not sure if that does make sense... could make it so it only affects npcs with at least one heart)
			// store current heart level for each npc listed in the villager network
			foreach (VillagerNetwork villager in Villagers.Values)
			{
				// if heart level has increased, give social network bonus to related npcs
				if (villager.HeartLevel < CurrentVillagerHearts(villager.Name))
					ChangeSocialNetworkBonus(villager, Config.SocialNetworkBonus);
				// if heart level has decreased, take away social network bonus from related npcs
				else if (villager.HeartLevel > CurrentVillagerHearts(villager.Name))
					ChangeSocialNetworkBonus(villager, -Config.SocialNetworkBonus);
			}
		}


		/// <summary>gets the heart level for the named npc</summary>
		/// <param name="npc name"></param>
		private static int CurrentVillagerHearts(string name) =>
			Game1.player.getFriendshipHeartLevelForNPC(name);


		/// <summary>Change friendship points with NPCs related to villager.</summary>
		/// <param name="villager">The original villager.</param>
		/// <param name="points">The number of points to add or remove from NPCs in their social network.</param>
		private void ChangeSocialNetworkBonus(VillagerNetwork villager, int points)
		{
			foreach (string relatedVillager in villager.Relationships)
			{
				Game1.player.changeFriendship(points, Game1.getCharacterFromName(relatedVillager));
				this.Monitor.Log($"{points} friendship to {relatedVillager}, due to your friendship with {villager.Name}", LogLevel.Info);
			}
		}


		/// <summary>Get all available characters, their relationships, and heart level.</summary>
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