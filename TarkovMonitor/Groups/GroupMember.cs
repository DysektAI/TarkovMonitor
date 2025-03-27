namespace TarkovMonitor.Groups
{
    /// <summary>
    /// Represents a member within a Tarkov raid group, containing their profile information and loadout.
    /// This class encapsulates the player's identity and their current equipment setup.
    /// </summary>
    class GroupMember
    {
        /// <summary>
        /// Gets the nickname/name of the group member.
        /// This is retrieved from the player's extended profile information.
        /// </summary>
        /// <returns>The player's nickname as a string.</returns>
        public string Name
        {
            get
            {
                return GroupMatchRaidReady.ExtendedProfile.Info.Nickname;
            }
        }

        /// <summary>
        /// Gets the visual representation of the player's current loadout.
        /// This includes their equipped items, armor, weapons, and other gear.
        /// </summary>
        /// <returns>A PlayerVisualRepresentation object containing the player's current equipment setup.</returns>
        public PlayerVisualRepresentation Loadout
        {
            get
            {
                return GroupMatchRaidReady.ExtendedProfile.PlayerVisualRepresentation;
            }
        }

        /// <summary>
        /// Private property that holds the raw group match data indicating the player's ready status
        /// and extended profile information.
        /// </summary>
        private GroupMatchRaidReadyLogContent GroupMatchRaidReady { get; }

        /// <summary>
        /// Initializes a new instance of the GroupMember class.
        /// </summary>
        /// <param name="memberReady">The GroupMatchRaidReadyLogContent containing the player's profile and ready status information.</param>
        /// <remarks>
        /// GroupMembers represent individual players within a raid group. Each member has their own
        /// loadout configuration and profile information that can be accessed through this class's properties.
        /// This class serves as a wrapper around the raw log content to provide easy access to commonly needed information.
        /// </remarks>
        public GroupMember(GroupMatchRaidReadyLogContent memberReady)
        {
            GroupMatchRaidReady = memberReady;
        }
    }
}
