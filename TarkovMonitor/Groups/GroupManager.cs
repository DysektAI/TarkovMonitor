// This namespace contains classes and functionality related to managing Escape from Tarkov group/party system
namespace TarkovMonitor.Groups
{
    /// <summary>
    /// Delegate for the group member change event.
    /// This is triggered whenever a member is added, removed, or the group state changes.
    /// </summary>
    /// <param name="source">The source object that triggered the event</param>
    /// <param name="e">Event arguments containing information about the change</param>
    public delegate void GroupMemberChanged(object source, GroupMemberChangedArgs e);

    /// <summary>
    /// Event arguments class for group member changes.
    /// Currently implements basic functionality but can be extended to include
    /// specific details about what changed in the group.
    /// </summary>
    public class GroupMemberChangedArgs : EventArgs
    {
        public GroupMemberChangedArgs()
        {
        }
    }

    /// <summary>
    /// Manages the state and operations of a Tarkov group/party.
    /// Handles adding, removing, and updating group members, and notifies subscribers of changes.
    /// </summary>
    class GroupManager
    {
        /// <summary>
        /// Dictionary storing all current group members.
        /// Key: Player nickname
        /// Value: GroupMember object containing player details
        /// </summary>
        public Dictionary<string, GroupMember> GroupMembers { get; set; } = new();

        /// <summary>
        /// Indicates if the current group state is stale/outdated.
        /// Used to determine if the group needs to be cleared before updates.
        /// </summary>
        public bool Stale { get; set; } = false;

        /// <summary>
        /// Removes a group member by their nickname and notifies subscribers of the change.
        /// </summary>
        /// <param name="name">The nickname of the player to remove</param>
        public void RemoveGroupMember(string name)
        {
            GroupMembers.Remove(name);
            GroupMemberChanged(this, new GroupMemberChangedArgs());
        }

        /// <summary>
        /// Updates or adds a group member based on raid ready log content.
        /// If the group is marked as stale and contains members, it will be cleared first.
        /// </summary>
        /// <param name="member">The member information from the raid ready log</param>
        public void UpdateGroupMember(GroupMatchRaidReadyLogContent member)
        {
            if (Stale && GroupMembers.Count > 0) ClearGroup();
            GroupMembers[member.ExtendedProfile.Info.Nickname] = new(member);
            GroupMemberChanged(this, new GroupMemberChangedArgs());
        }

        /// <summary>
        /// Removes all members from the group and notifies subscribers of the change.
        /// Used when needing to reset the group state or when the group is disbanded.
        /// </summary>
        public void ClearGroup()
        {
            GroupMembers.Clear();
            GroupMemberChanged(this, new GroupMemberChangedArgs());
        }

        /// <summary>
        /// Event that is triggered whenever the group composition changes.
        /// Subscribers can use this to react to changes in the group state.
        /// The delegate is initialized with an empty implementation to prevent null reference exceptions.
        /// </summary>
        public event GroupMemberChanged GroupMemberChanged = delegate { };
    }
}
