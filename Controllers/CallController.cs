using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Models;

using System.Security.Claims;

namespace Site.Controllers
{
    public class CallController : Controller
    {
        private readonly AppDbContext _db;

        public CallController(AppDbContext db)
        {
            _db = db;
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

            var log = new CallLog
            {
                CallerId = CurrentUserId.Value,
                ReceiverId = receiverId,
                CallType = callType,   // "Audio" or "Video"
                Status = status,        // "Missed", "Completed", "Rejected"
                DurationSeconds = durationSeconds,
                StartedAt = DateTime.UtcNow
            };

            _db.CallLogs.Add(log);
            await _db.SaveChangesAsync();

            return Json(new { success = true, logId = log.Id });
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
