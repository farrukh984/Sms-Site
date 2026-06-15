using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Hubs;
using Site.Models;

using System.Security.Claims;

namespace Site.Controllers
{
    public class CallController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<ChatHub> _hub;

        public CallController(AppDbContext db, IHubContext<ChatHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // ─── Guard ──────────────────────────────────────────────────
        private int? CurrentUserId
        {
            get
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdString, out int id)) return id;
                return null;
            }
        }

        public async Task<IActionResult> Index()
        {
            if (CurrentUserId == null) return RedirectToAction("Login", "Auth");

            var calls = await _db.CallLogs
                .Include(c => c.Caller)
                .Include(c => c.Receiver)
                .Where(c => c.CallerId == CurrentUserId || c.ReceiverId == CurrentUserId)
                .OrderByDescending(c => c.StartedAt)
                .Take(50)
                .ToListAsync();

            var currentUser = await _db.Users.FindAsync(CurrentUserId.Value);
            ViewBag.CurrentUserId = CurrentUserId;
            ViewBag.CurrentUser = currentUser;

            return View(calls);
        }

        [HttpPost]
        public async Task<IActionResult> LogCall(int receiverId, string callType, string status, int durationSeconds)
        {
            if (CurrentUserId == null) return Json(new { success = false });

            var callerId = CurrentUserId.Value;

            // 1. Save CallLog as before
            var log = new CallLog
            {
                CallerId = callerId,
                ReceiverId = receiverId,
                CallType = callType,   // "Audio" or "Video"
                Status = status,        // "Missed", "Completed", "Rejected"
                DurationSeconds = durationSeconds,
                StartedAt = DateTime.UtcNow
            };

            _db.CallLogs.Add(log);
            await _db.SaveChangesAsync();

            // 2. Build the call message content  e.g. "Voice call|No answer" or "Video call|22 min"
            string callTypeName = callType == "Video" ? "Video call" : "Voice call";
            string resultStr;
            if (status == "Completed" && durationSeconds > 0)
            {
                if (durationSeconds >= 3600)
                    resultStr = $"{durationSeconds / 3600}h {(durationSeconds % 3600) / 60}m";
                else if (durationSeconds >= 60)
                    resultStr = $"{durationSeconds / 60} min";
                else
                    resultStr = $"{durationSeconds} sec";
            }
            else
            {
                resultStr = "No answer";
            }
            var callContent = $"{callTypeName}|{resultStr}";

            // 3. Find existing 1-on-1 chat between caller and receiver
            var existingChatId = await _db.ChatParticipants
                .Where(cp => cp.UserId == callerId)
                .Select(cp => cp.ChatId)
                .FirstOrDefaultAsync(chatId =>
                    _db.ChatParticipants.Any(cp2 => cp2.ChatId == chatId && cp2.UserId == receiverId) &&
                    !_db.Chats.Where(c => c.Id == chatId).Select(c => c.IsGroup).FirstOrDefault());

            int chatId = existingChatId;

            // 4. If no chat exists, create one
            if (chatId == 0)
            {
                var newChat = new Chat { IsGroup = false, CreatedAt = DateTime.UtcNow };
                _db.Chats.Add(newChat);
                await _db.SaveChangesAsync();
                _db.ChatParticipants.AddRange(
                    new ChatParticipant { ChatId = newChat.Id, UserId = callerId },
                    new ChatParticipant { ChatId = newChat.Id, UserId = receiverId }
                );
                await _db.SaveChangesAsync();
                chatId = newChat.Id;
            }

            // 5. Save Message of type Call into chat
            var caller = await _db.Users.FindAsync(callerId);
            var callMessage = new Message
            {
                ChatId = chatId,
                SenderId = callerId,
                Content = callContent,
                Type = MessageType.Call,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };
            _db.Messages.Add(callMessage);
            await _db.SaveChangesAsync();

            // 6. Broadcast the call message via SignalR to both participants
            var senderName = caller?.FullName ?? caller?.Username ?? "Unknown";
            var participantIds = await _db.ChatParticipants
                .Where(cp => cp.ChatId == chatId)
                .Select(cp => cp.UserId)
                .ToListAsync();

            foreach (var pId in participantIds)
            {
                await _hub.Clients.Group($"User_{pId}").SendAsync(
                    "ReceiveMessage",
                    chatId,
                    callerId,
                    senderName,
                    callContent,
                    "Call",
                    (string?)null,
                    callMessage.SentAt.ToString("o"),
                    callMessage.Id,
                    (int?)null,
                    (string?)null
                );
            }

            return Json(new { success = true, logId = log.Id, messageId = callMessage.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetCallLogs()
        {
            if (CurrentUserId == null) return Json(new List<object>());

            var calls = await _db.CallLogs
                .Include(c => c.Caller)
                .Include(c => c.Receiver)
                .Where(c => c.CallerId == CurrentUserId || c.ReceiverId == CurrentUserId)
                .OrderByDescending(c => c.StartedAt)
                .Take(50)
                .ToListAsync();

            var result = calls.Select(c =>
            {
                var isOutgoing = c.CallerId == CurrentUserId;
                var otherUser = isOutgoing ? c.Receiver : c.Caller;
                return new
                {
                    id = c.Id,
                    callType = c.CallType,
                    status = c.Status,
                    durationSeconds = c.DurationSeconds,
                    startedAt = c.StartedAt,
                    isOutgoing = isOutgoing,
                    otherUserId = otherUser?.Id,
                    otherUserName = otherUser?.FullName ?? otherUser?.Username ?? "Unknown",
                    otherUserPic = otherUser?.ProfilePicture ?? "/images/default-avatar.svg"
                };
            });

            return Json(result);
        }
    }
}
