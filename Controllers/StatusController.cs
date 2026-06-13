using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Models;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;

using Site.Services;

namespace Site.Controllers
{
    [Authorize]
    public class StatusController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ImageUploadService _imageUploadService;

        public StatusController(AppDbContext db, ImageUploadService imageUploadService)
        {
            _db = db;
            _imageUploadService = imageUploadService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int currentUserId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            ViewBag.CurrentUser = currentUser;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetMyStatuses()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var now = DateTime.UtcNow;
            var statuses = await _db.Statuses
                .Where(s => s.UserId == currentUserId && s.ExpiresAt > now)
                .OrderBy(s => s.CreatedAt)
                .Select(s => new
                {
                    s.Id,
                    s.Type,
                    s.MediaUrl,
                    s.TextContent,
                    s.BackgroundColor,
                    s.FontStyle,
                    s.CreatedAt,
                    ViewsCount = s.StatusViewers.Count()
                })
                .ToListAsync();

            return Json(statuses);
        }

        [HttpGet]
        public async Task<IActionResult> GetContactStatuses()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var now = DateTime.UtcNow;
            // Get statuses of other users that haven't expired
            var statuses = await _db.Statuses
                .Include(s => s.User)
                .Include(s => s.StatusViewers)
                .Where(s => s.UserId != currentUserId && s.ExpiresAt > now)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            // Group by User
            var groupedStatuses = statuses.GroupBy(s => s.UserId).Select(g => new
            {
                UserId = g.Key,
                Username = g.First().User?.Username,
                FullName = g.First().User?.FullName ?? g.First().User?.Username,
                Avatar = g.First().User?.ProfilePicture ?? "/images/default-avatar.svg",
                Statuses = g.Select(s => new
                {
                    s.Id,
                    s.Type,
                    s.MediaUrl,
                    s.TextContent,
                    s.BackgroundColor,
                    s.FontStyle,
                    s.CreatedAt,
                    IsViewed = s.StatusViewers.Any(sv => sv.ViewerId == currentUserId)
                }).ToList()
            }).ToList();

            return Json(groupedStatuses);
        }

        [HttpPost]
        public async Task<IActionResult> PostStatus(IFormFile? file, string type, string? textContent, string? backgroundColor, string? fontStyle)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var status = new Status
            {
                UserId = currentUserId,
                Type = type,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(1) // 24 hours
            };

            if (type == "Text")
            {
                status.TextContent = textContent;
                status.BackgroundColor = string.IsNullOrEmpty(backgroundColor) ? "#25D366" : backgroundColor;
                status.FontStyle = string.IsNullOrEmpty(fontStyle) ? "Arial" : fontStyle;
            }
            else if (file != null && file.Length > 0)
            {
                status.MediaUrl = await _imageUploadService.UploadFileAsync(file, "statuses");
            }
            else
            {
                return BadRequest("No file or text content provided.");
            }

            _db.Statuses.Add(status);
            await _db.SaveChangesAsync();

            return Json(new { success = true, statusId = status.Id });
        }

        [HttpPost]
        public async Task<IActionResult> ViewStatus(int statusId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify status exists and hasn't expired
            var status = await _db.Statuses.FindAsync(statusId);
            if (status == null || status.ExpiresAt < DateTime.UtcNow) return NotFound();

            // Check if already viewed
            var alreadyViewed = await _db.StatusViewers
                .AnyAsync(sv => sv.StatusId == statusId && sv.ViewerId == currentUserId);

            if (!alreadyViewed && status.UserId != currentUserId)
            {
                var viewer = new StatusViewer
                {
                    StatusId = statusId,
                    ViewerId = currentUserId,
                    ViewedAt = DateTime.UtcNow
                };
                _db.StatusViewers.Add(viewer);
                await _db.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStatus(int statusId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var status = await _db.Statuses.FindAsync(statusId);
            if (status == null) return NotFound();

            if (status.UserId != currentUserId) return Forbid();

            _db.Statuses.Remove(status);
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetStatusViewers(int statusId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var status = await _db.Statuses.FindAsync(statusId);
            if (status == null || status.UserId != currentUserId) return Forbid();

            var viewers = await _db.StatusViewers
                .Include(sv => sv.Viewer)
                .Where(sv => sv.StatusId == statusId)
                .Select(sv => new
                {
                    sv.ViewerId,
                    Username = sv.Viewer.Username,
                    FullName = sv.Viewer.FullName ?? sv.Viewer.Username,
                    Avatar = sv.Viewer.ProfilePicture ?? "/images/default-avatar.svg",
                    ViewedAt = sv.ViewedAt.ToString("g")
                })
                .ToListAsync();

            return Json(viewers);
        }
    }
}
