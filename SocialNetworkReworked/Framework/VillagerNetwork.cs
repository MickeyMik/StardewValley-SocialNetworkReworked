using System.Collections.Generic;

namespace SocialNetworkReworked.Framework;

/// <summary>Tracked data for an NPC.</summary>
internal class VillagerNetwork
{
    /*********
    ** Accessors
    *********/
    /// <summary>The NPC name.</summary>
    public string Name { get; }

    public int HeartLevel { get; }

    /// <summary>The NPC's relationships with other NPCs.</summary>
    public IList<string> Relationships { get; } = new List<string>();


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="name">The NPC name.</param>
    public VillagerNetwork(string name, int heartLevel, Dictionary<string, string> relationships)
    {
        this.Name = name;
        this.HeartLevel = heartLevel;
        foreach (string relation in relationships.Keys)
            this.Relationships.Add(relation);
    }
}
