using ChurchMinistry.Models;
using Microsoft.AspNetCore.SignalR;

namespace ChurchMinistry.Hubs
{
    /// <summary>
    /// Sends a message that carries a member's name to a SignalR group in two versions:
    /// Admins and Pastors ("staff") get real names, everyone else gets aliases
    /// (see MemberNames). A connection that joins group g through JoinAsync is also put in
    /// "g|staff" or "g|members", and name-carrying messages go to those halves. Messages
    /// without names can still go to g itself.
    /// </summary>
    public static class HubAudience
    {
        public static string Staff(string group) => group + "|staff";
        public static string Members(string group) => group + "|members";

        public static async Task JoinAsync(HubCallerContext context, IGroupManager groups, string group)
        {
            await groups.AddToGroupAsync(context.ConnectionId, group);
            await groups.AddToGroupAsync(context.ConnectionId,
                context.User.IsStaff() ? Staff(group) : Members(group));
        }

        public static Task LeaveAsync(HubCallerContext context, IGroupManager groups, string group)
            => LeaveAsync(groups, context.ConnectionId, group);

        // Takes any connection out of the group and both its halves
        public static async Task LeaveAsync(IGroupManager groups, string connectionId, string group)
        {
            await groups.RemoveFromGroupAsync(connectionId, group);
            await groups.RemoveFromGroupAsync(connectionId, Staff(group));
            await groups.RemoveFromGroupAsync(connectionId, Members(group));
        }

        /// <summary>
        /// Sends method to everyone in group (minus except): args(true) to staff, args(false)
        /// to everyone else.
        /// </summary>
        public static Task SendAsync(IHubClients<IClientProxy> clients, string group, string method,
            Func<bool, object?[]> args, params string[] except)
            => Task.WhenAll(
                clients.GroupExcept(Staff(group), except).SendCoreAsync(method, args(true)),
                clients.GroupExcept(Members(group), except).SendCoreAsync(method, args(false)));
    }
}
