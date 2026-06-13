using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Linq;
using Site.Data;

namespace Site.Hubs
{
    public class ChatHub : Hub
    {
        private readonly AppDbContext _db;

        public ChatHub(AppDbContext db)
        {
            _db = db;
        }

        public static ConcurrentDictionary<string, int> OnlineUsers = new ConcurrentDictionary<string, int>();

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                OnlineUsers.AddOrUpdate(userId, 1, (key, count) => count + 1);
                if (OnlineUsers[userId] == 1) 
                {
                    await Clients.All.SendAsync("UserOnlineStatus", userId, true, null);
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                if (OnlineUsers.TryGetValue(userId, out int count))
                {
                    if (count > 1)
                    {
                        OnlineUsers[userId] = count - 1;
                    }
                    else
                    {
                        OnlineUsers.TryRemove(userId, out _);
                        if (int.TryParse(userId, out int uId))
                        {
                            var user = await _db.Users.FindAsync(uId);
                            if (user != null)
                            {
                                user.LastSeen = DateTime.UtcNow;
                                await _db.SaveChangesAsync();
                                await Clients.All.SendAsync("UserOnlineStatus", userId, false, user.LastSeen.Value.ToString("o"));
                                return;
                            }
                        }
                        await Clients.All.SendAsync("UserOnlineStatus", userId, false, null);
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task CheckUserOnline(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            bool isOnline = OnlineUsers.ContainsKey(userId);
            string? lastSeenStr = null;

            if (!isOnline && int.TryParse(userId, out int uId))
            {
                var user = await _db.Users.FindAsync(uId);
                if (user != null && user.LastSeen.HasValue)
                {
                    lastSeenStr = user.LastSeen.Value.ToString("o");
                }
            }

            await Clients.Caller.SendAsync("UserOnlineStatus", userId, isOnline, lastSeenStr);
        }

        public async Task JoinChat(string chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId);
        }

        public async Task LeaveChat(string chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId);
        }

        public async Task SendMessage(string chatId, string senderId, string senderName, string content, string type, string? fileUrl, int messageId, int? replyToMessageId, string? replyToMessageContent)
        {
            await Clients.Group(chatId).SendAsync("ReceiveMessage", chatId, senderId, senderName, content, type, fileUrl, DateTime.UtcNow.ToString("o"), messageId, replyToMessageId, replyToMessageContent);
        }

        public async Task BroadcastDelete(string chatId, int messageId)
        {
            await Clients.Group(chatId).SendAsync("MessageDeleted", chatId, messageId);
        }

        public async Task BroadcastReaction(string chatId, int messageId, string reactionsJson)
        {
            await Clients.Group(chatId).SendAsync("MessageReacted", chatId, messageId, reactionsJson);
        }

        public async Task SendTyping(string chatId, string userId, bool isTyping)
        {
            await Clients.Group(chatId).SendAsync("UserTyping", chatId, userId, isTyping);
        }

        public async Task SendRecording(string chatId, string userId, bool isRecording)
        {
            await Clients.Group(chatId).SendAsync("UserRecording", chatId, userId, isRecording);
        }

        public async Task TriggerCall(string chatId, string callerId, string callerName, string callType)
        {
            await Clients.Group(chatId).SendAsync("IncomingCall", chatId, callerId, callerName, callType);
        }

        public async Task AnswerCall(string chatId, bool accepted)
        {
            await Clients.Group(chatId).SendAsync("CallAnswered", chatId, accepted);
        }

        public async Task EndCall(string chatId)
        {
            await Clients.Group(chatId).SendAsync("CallEnded", chatId);
        }

        public async Task SendWebRTCSignal(string chatId, string senderId, string type, string payload)
        {
            await Clients.Group(chatId).SendAsync("ReceiveWebRTCSignal", chatId, senderId, type, payload);
        }
    }
}
