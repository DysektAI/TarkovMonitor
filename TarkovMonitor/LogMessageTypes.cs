// This file contains class definitions for parsing and handling various types of log messages
// from the Escape from Tarkov game. It defines the structure of different message types
// and their associated data models.
namespace TarkovMonitor
{
    /// <summary>
    /// Base class for all JSON log content. Provides common properties for all log messages.
    /// </summary>
    public class JsonLogContent
    {
        /// <summary>
        /// The type of the log message
        /// </summary>
        public string Type { get; set; } = null!;

        /// <summary>
        /// Unique identifier for the event
        /// </summary>
        public string EventId { get; set; } = null!;
    }

    /// <summary>
    /// Represents a log message when a user leaves a group match
    /// </summary>
    public class GroupMatchUserLeaveLogContent : JsonLogContent
    {
        /// <summary>
        /// The nickname of the player who left. Defaults to "You" if it's the local player
        /// </summary>
        public string Nickname { get; set; } = "You";
    }

    /// <summary>
    /// Represents basic group-related log content
    /// </summary>
    public class GroupLogContent : JsonLogContent
    {
        /// <summary>
        /// Information about the player in the group
        /// </summary>
        public PlayerInfo Info { get; set; } = null!;

        /// <summary>
        /// Indicates if the player is the group leader
        /// </summary>
        public bool IsLeader { get; set; }
    }

    /// <summary>
    /// Represents a log message when a player is ready for a raid in a group match
    /// </summary>
    public class GroupMatchRaidReadyLogContent : JsonLogContent
    {
        /// <summary>
        /// Detailed profile information of the ready player
        /// </summary>
        public ExtendedProfile ExtendedProfile { get; set; } = null!;

        /// <summary>
        /// Returns a formatted string with player information: "Nickname (Side, Level)"
        /// </summary>
        public override string ToString()
        {
            return $"{ExtendedProfile.Info.Nickname} ({ExtendedProfile.PlayerVisualRepresentation.Info.Side}, {ExtendedProfile.PlayerVisualRepresentation.Info.Level})";
        }
    }

    /// <summary>
    /// Contains extended profile information for a player, including visual representation
    /// </summary>
    public class ExtendedProfile
    {
        /// <summary>
        /// Basic player information
        /// </summary>
        public PlayerInfo Info { get; set; } = null!;

        /// <summary>
        /// Indicates if this player is the group leader
        /// </summary>
        public bool IsLeader { get; set; }

        /// <summary>
        /// Visual representation data including equipment and customization
        /// </summary>
        public PlayerVisualRepresentation PlayerVisualRepresentation { get; set; } = null!;
    }

    /// <summary>
    /// Contains basic player information such as level, faction (side), and nickname
    /// </summary>
    public class PlayerInfo
    {
        /// <summary>
        /// Player's faction (BEAR/USEC/SCAV)
        /// </summary>
        public string Side { get; set; } = null!;

        /// <summary>
        /// Player's current level
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Player's display name
        /// </summary>
        public string Nickname { get; set; } = null!;

        /// <summary>
        /// Category of the player's membership
        /// </summary>
        public int MemberCategory { get; set; }
    }

    /// <summary>
    /// Represents the visual appearance of a player including equipment and clothing
    /// </summary>
    public class PlayerVisualRepresentation
    {
        /// <summary>
        /// Basic player information
        /// </summary>
        public PlayerInfo Info { get; set; } = null!;

        /// <summary>
        /// Player's equipped items
        /// </summary>
        public PlayerEquipment Equipment { get; set; } = null!;

        /// <summary>
        /// Player's clothing/cosmetic items
        /// </summary>
        public PlayerClothes Customization { get; set; } = null!;
    }

    /// <summary>
    /// Represents a player's equipped items and loadout
    /// </summary>
    public class PlayerEquipment
    {
        /// <summary>
        /// Unique identifier for the equipment set
        /// </summary>
        public string Id { get; set; } = null!;

        /// <summary>
        /// Array of items in the loadout
        /// </summary>
        public LoadoutItem[] Items { get; set; } = null!;
    }

    /// <summary>
    /// Represents an individual item in a player's loadout with its properties
    /// </summary>
    public class LoadoutItem
    {
        /// <summary>
        /// Unique instance ID of the item
        /// </summary>
        public string _id { get; set; } = null!;

        /// <summary>
        /// Template ID of the item (item type)
        /// </summary>
        public string _tpl { get; set; } = null!;

        /// <summary>
        /// ID of the container/slot this item is in
        /// </summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// Specific slot identifier where the item is equipped
        /// </summary>
        public string? SlotId { get; set; }

        /// <summary>
        /// Custom name of the item (if renamed)
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Additional properties of the item
        /// </summary>
        public LoadoutItemProperties? Upd { get; set; }

        /// <summary>
        /// Returns a formatted string representation of the item including stack size and durability if applicable
        /// </summary>
        public override string ToString()
        {
            var displayName = _tpl;
            if (Name != null) displayName = Name;
            if (Upd?.StackObjectsCount > 1) displayName += $" (x{Upd.StackObjectsCount})";
            if (Upd?.Repairable != null) displayName += $" ({Math.Round(Upd.Repairable.Durability, 2)}/{Upd.Repairable.MaxDurability})";
            return displayName;
        }
    }

    /// <summary>
    /// Represents the location of an item in a grid-based inventory system
    /// </summary>
    public class LoadoutItemLocation
    {
        /// <summary>
        /// Horizontal position in the grid
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Vertical position in the grid
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Rotation of the item (in degrees)
        /// </summary>
        public int R { get; set; }

        /// <summary>
        /// Indicates if the item has been examined by the player
        /// </summary>
        public bool IsSearched { get; set; }
    }

    /// <summary>
    /// Container class for various item properties that can be attached to items
    /// </summary>
    public class LoadoutItemProperties
    {
        /// <summary>
        /// Number of items in a stack
        /// </summary>
        public int? StackObjectsCount { get; set; }

        /// <summary>
        /// Indicates if the item was found in the current raid
        /// </summary>
        public bool? SpawnedInSession { get; set; }

        /// <summary>
        /// Durability information for repairable items
        /// </summary>
        public LoadoutItemPropertiesDurability? Repairable { get; set; }

        /// <summary>
        /// Properties for medical items
        /// </summary>
        public LoadoutItemPropertiesHpResource? MedKit { get; set; }

        /// <summary>
        /// Properties for consumable items (food/drinks)
        /// </summary>
        public LoadoutItemPropertiesHpResource? FoodDrink { get; set; }

        /// <summary>
        /// Properties for weapons' fire modes
        /// </summary>
        public LoadoutItemPropertiesFireMode? FireMode { get; set; }

        /// <summary>
        /// Properties for weapon sights/scopes
        /// </summary>
        public LoadoutItemPropertiesScope? Sight { get; set; }

        /// <summary>
        /// Properties for items with limited uses (e.g., fuel)
        /// </summary>
        public LoadoutItemPropertiesResource? Resource { get; set; }

        /// <summary>
        /// Properties for dogtags
        /// </summary>
        public LoadoutItemPropertiesDogtag? Dogtag { get; set; }

        /// <summary>
        /// Properties for item tags (custom labels)
        /// </summary>
        public LoadoutItemPropertiesTag? Tag { get; set; }

        /// <summary>
        /// Properties for keys and keycards
        /// </summary>
        public LoadoutItemPropertiesKey? Key { get; set; }
    }

    /// <summary>
    /// Represents durability information for items that can be repaired
    /// </summary>
    public class LoadoutItemPropertiesDurability
    {
        /// <summary>
        /// Maximum durability the item can have
        /// </summary>
        public float MaxDurability { get; set; }

        /// <summary>
        /// Current durability of the item
        /// </summary>
        public float Durability { get; set; }
    }

    /// <summary>
    /// Represents resources for medical items and consumables
    /// </summary>
    public class LoadoutItemPropertiesHpResource
    {
        /// <summary>
        /// Amount of resource remaining (e.g., medical uses or food points)
        /// </summary>
        public int HpResource { get; set; }
    }

    /// <summary>
    /// Represents weapon fire mode settings
    /// </summary>
    public class LoadoutItemPropertiesFireMode
    {
        /// <summary>
        /// Current fire mode (e.g., single, burst, auto)
        /// </summary>
        public string? FireMode { get; set; }
    }

    /// <summary>
    /// Represents scope/sight settings on weapons
    /// </summary>
    public class LoadoutItemPropertiesScope
    {
        /// <summary>
        /// Current zeroing settings for each scope
        /// </summary>
        public List<int>? ScopesCurrentCalibPointIndexes { get; set; }

        /// <summary>
        /// Selected modes for variable zoom scopes
        /// </summary>
        public List<int>? ScopesSelectedModes { get; set; }

        /// <summary>
        /// Currently active scope index
        /// </summary>
        public int? SelectedScope { get; set; }
    }

    /// <summary>
    /// Represents resource count for items with limited uses
    /// </summary>
    public class LoadoutItemPropertiesResource
    {
        /// <summary>
        /// Amount of resource remaining
        /// </summary>
        public int Value { get; set; }
    }

    /// <summary>
    /// Represents properties of a player's dogtag
    /// </summary>
    public class LoadoutItemPropertiesDogtag
    {
        /// <summary>
        /// Account ID of the dogtag owner
        /// </summary>
        public string? AccountId { get; set; }

        /// <summary>
        /// Profile ID of the dogtag owner
        /// </summary>
        public string? ProfileId { get; set; }

        /// <summary>
        /// Faction of the dogtag owner
        /// </summary>
        public string? Side { get; set; }

        /// <summary>
        /// Level of the dogtag owner
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Time of death
        /// </summary>
        public string? Time { get; set; }

        /// <summary>
        /// Status of the kill
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Account ID of the killer
        /// </summary>
        public string? KillerAccountId { get; set; }

        /// <summary>
        /// Profile ID of the killer
        /// </summary>
        public string? KillerProfileId { get; set; }

        /// <summary>
        /// Name of the killer
        /// </summary>
        public string? KillerName { get; set; }

        /// <summary>
        /// Weapon used for the kill
        /// </summary>
        public string? WeaponName { get; set; }
    }

    /// <summary>
    /// Represents custom tags that can be added to items
    /// </summary>
    public class LoadoutItemPropertiesTag
    {
        /// <summary>
        /// Custom label text
        /// </summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Represents properties specific to keys and keycards
    /// </summary>
    public class LoadoutItemPropertiesKey
    {
        /// <summary>
        /// Number of times the key has been used
        /// </summary>
        public int NumberOfUsages { get; set; }
    }

    /// <summary>
    /// Represents a player's clothing customization
    /// </summary>
    public class PlayerClothes
    {
        /// <summary>
        /// Head clothing/gear ID
        /// </summary>
        public string Head { get; set; } = null!;

        /// <summary>
        /// Body clothing ID
        /// </summary>
        public string Body { get; set; } = null!;

        /// <summary>
        /// Feet clothing/boots ID
        /// </summary>
        public string Feet { get; set; } = null!;

        /// <summary>
        /// Hands clothing/gloves ID
        /// </summary>
        public string Hands { get; set; } = null!;
    }

    /// <summary>
    /// Represents raid settings for a group, including map and raid type information
    /// </summary>
    public class GroupRaidSettingsLogContent : JsonLogContent
    {
        /// <summary>
        /// Gets the map location for the raid
        /// </summary>
        public string Map
        {
            get
            {
                return raidSettings.Location;
            }
        }

        /// <summary>
        /// Gets the raid mode (e.g., online, offline)
        /// </summary>
        public string RaidMode
        {
            get
            {
                return raidSettings.RaidMode;
            }
        }

        /// <summary>
        /// Gets the type of raid (PMC, Scav, or Unknown)
        /// </summary>
        public RaidType RaidType
        {
            get
            {
                if (raidSettings.Side == "Pmc")
                {
                    return RaidType.PMC;
                }
                if (raidSettings.Side == "Savage")
                {
                    return RaidType.Scav;
                }
                return RaidType.Unknown;
            }
        }

        /// <summary>
        /// The raid settings configuration
        /// </summary>
        public RaidSettings raidSettings { get; set; } = null!;

        /// <summary>
        /// Nested class containing raid configuration details
        /// </summary>
        public class RaidSettings
        {
            /// <summary>
            /// The map location for the raid
            /// </summary>
            public string Location { get; set; } = null!;

            /// <summary>
            /// The mode of the raid (online/offline)
            /// </summary>
            public string RaidMode { get; set; } = null!;

            /// <summary>
            /// The side/faction for the raid (Pmc/Savage)
            /// </summary>
            public string Side { get; set; } = null!;
        }
    }

    /// <summary>
    /// Base class for chat message log content
    /// </summary>
    public class ChatMessageLogContent : JsonLogContent
    {
        /// <summary>
        /// The chat message content
        /// </summary>
        public ChatMessage Message { get; set; } = null!;
    }

    /// <summary>
    /// Represents a chat message with type and content
    /// </summary>
    public class ChatMessage
    {
        /// <summary>
        /// The type of message
        /// </summary>
        public MessageType Type { get; set; }

        /// <summary>
        /// The message text content
        /// </summary>
        public string Text { get; set; } = null!;

        /// <summary>
        /// Indicates if the message includes reward information
        /// </summary>
        public bool HasRewards { get; set; }
    }

    /// <summary>
    /// Represents a system-generated chat message
    /// </summary>
    public class SystemChatMessage : ChatMessage
    {
        /// <summary>
        /// The template identifier for the system message
        /// </summary>
        public string TemplateId { get; set; } = null!;
    }

    /// <summary>
    /// Log content specifically for system chat messages
    /// </summary>
    public class SystemChatMessageLogContent : ChatMessageLogContent
    {
        /// <summary>
        /// The system message content
        /// </summary>
        public new SystemChatMessage Message { get; set; } = null!;
    }

    /// <summary>
    /// System chat message that includes item information
    /// </summary>
    public class SystemChatMessageWithItems : SystemChatMessage
    {
        /// <summary>
        /// The items associated with the message
        /// </summary>
        public MessageItems Items { get; set; } = null!;
    }

    /// <summary>
    /// Container for items in a message
    /// </summary>
    public class MessageItems
    {
        /// <summary>
        /// List of items in the message
        /// </summary>
        public List<LoadoutItem> Data { get; set; } = null!;
    }

    /// <summary>
    /// Represents a chat message for items sold on the flea market
    /// </summary>
    public class FleaMarketSoldChatMessage : SystemChatMessageWithItems
    {
        /// <summary>
        /// Additional data about the flea market sale
        /// </summary>
        public FleaSoldData SystemData { get; set; } = null!;
    }

    /// <summary>
    /// Contains details about a flea market sale
    /// </summary>
    public class FleaSoldData
    {
        /// <summary>
        /// Name of the player who bought the item
        /// </summary>
        public string BuyerNickname { get; set; } = null!;

        /// <summary>
        /// Identifier of the item that was sold
        /// </summary>
        public string SoldItem { get; set; } = null!;

        /// <summary>
        /// Quantity of items sold
        /// </summary>
        public int ItemCount { get; set; }
    }

    /// <summary>
    /// Log content for flea market sales with convenient property accessors
    /// </summary>
    public class FleaSoldMessageLogContent : SystemChatMessageLogContent
    {
        /// <summary>
        /// Gets the buyer's nickname
        /// </summary>
        public string Buyer
        {
            get
            {
                return Message.SystemData.BuyerNickname;
            }
        }

        /// <summary>
        /// Gets the ID of the sold item
        /// </summary>
        public string SoldItemId
        {
            get
            {
                return Message.SystemData.SoldItem;
            }
        }

        /// <summary>
        /// Gets the quantity of items sold
        /// </summary>
        public int SoldItemCount
        {
            get
            {
                return Message.SystemData.ItemCount;
            }
        }

        /// <summary>
        /// Gets a dictionary of received items and their quantities
        /// </summary>
        public Dictionary<string, int> ReceivedItems
        {
            get
            {
                Dictionary<string, int> items = new();
                foreach (var item in Message.Items.Data)
                {
                    if (items.ContainsKey(item._tpl))
                    {
                        // when large amounts of roubles are paid, they are paid stacks of 500k maximum
                        // which means there may be multiple received "items" of roubles per sale
                        items[item._tpl] += item.Upd?.StackObjectsCount ?? 1;
                        continue;
                    }
                    items.Add(item._tpl, item.Upd?.StackObjectsCount ?? 1);
                }
                return items;
            }
        }

        /// <summary>
        /// The flea market sale message content
        /// </summary>
        public new FleaMarketSoldChatMessage Message { get; set; } = null!;
    }

    /// <summary>
    /// Log content for expired flea market listings
    /// </summary>
    public class FleaExpiredMessageLogContent : JsonLogContent
    {
        /// <summary>
        /// Gets the ID of the expired item
        /// </summary>
        public string ItemId
        {
            get
            {
                return Message.Items.Data[0]._id;
            }
        }

        /// <summary>
        /// Gets the quantity of expired items
        /// </summary>
        public int ItemCount
        {
            get
            {
                return Message.Items.Data[0].Upd?.StackObjectsCount ?? 1;
            }
        }

        /// <summary>
        /// The expired listing message content
        /// </summary>
        public SystemChatMessageWithItems Message { get; set; } = null!;
    }

    /// <summary>
    /// Log content for task/quest status updates
    /// </summary>
    public class TaskStatusMessageLogContent : ChatMessageLogContent
    {
        /// <summary>
        /// Gets the task/quest identifier
        /// </summary>
        public string TaskId
        {
            get
            {
                return Message.TemplateId.Split(' ')[0];
            }
        }

        /// <summary>
        /// Gets the current status of the task
        /// </summary>
        public TaskStatus Status
        {
            get
            {
                return (TaskStatus)Message.Type;
            }
        }

        /// <summary>
        /// The task status message content
        /// </summary>
        public new SystemChatMessage Message { get; set; } = null!;
    }
}
