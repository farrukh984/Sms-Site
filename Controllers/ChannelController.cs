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
    public class ChannelController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ImageUploadService _imageUploadService;

        public ChannelController(AppDbContext db, ImageUploadService imageUploadService)
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
        public async Task<IActionResult> GetMyChannels()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Channels this user owns or follows
            var followedChannelIds = await _db.ChannelFollowers
                .Where(cf => cf.UserId == currentUserId)
                .Select(cf => cf.ChannelId)
                .ToListAsync();

            var channels = await _db.Channels
                .Where(c => c.OwnerId == currentUserId || followedChannelIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    Avatar = c.Avatar ?? "/images/default-avatar.svg",
                    c.OwnerId,
                    IsOwner = c.OwnerId == currentUserId,
                    FollowersCount = c.Followers.Count(),
                    LastMessage = _db.ChannelMessages
                        .Where(cm => cm.ChannelId == c.Id)
                        .OrderByDescending(cm => cm.SentAt)
                        .Select(cm => new
                        {
                            cm.Content,
                            cm.SentAt,
                            cm.Type
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var formatted = channels.Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                c.Avatar,
                c.OwnerId,
                c.IsOwner,
                c.FollowersCount,
                LastMessageText = c.LastMessage != null 
                    ? (c.LastMessage.Type == "Text" ? c.LastMessage.Content : $"[Broadcast {c.LastMessage.Type}]") 
                    : "No broadcasts yet",
                LastMessageTime = c.LastMessage?.SentAt.ToString("o")
            }).ToList();

            return Json(formatted);
        }

        [HttpGet]
        public async Task<IActionResult> DiscoverChannels()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Channels this user doesn't own and doesn't follow
            var followedChannelIds = await _db.ChannelFollowers
                .Where(cf => cf.UserId == currentUserId)
                .Select(cf => cf.ChannelId)
                .ToListAsync();

            var channels = await _db.Channels
                .Where(c => c.OwnerId != currentUserId && !followedChannelIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    Avatar = c.Avatar ?? "/images/default-avatar.svg",
                    FollowersCount = c.Followers.Count()
                })
                .ToListAsync();

            return Json(channels);
        }

        [HttpPost]
        public async Task<IActionResult> CreateChannel(string name, string? description, IFormFile? avatar)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            if (string.IsNullOrEmpty(name)) return BadRequest("Channel name is required.");

            var channel = new Channel
            {
                Name = name,
                Description = description,
                OwnerId = currentUserId,
                CreatedAt = DateTime.UtcNow
            };

            if (avatar != null && avatar.Length > 0)
            {
                channel.Avatar = await _imageUploadService.UploadFileAsync(avatar, "channels");
            }

            _db.Channels.Add(channel);
            await _db.SaveChangesAsync();

            // Automatically follow own channel
            var follower = new ChannelFollower
            {
                ChannelId = channel.Id,
                UserId = currentUserId,
                JoinedAt = DateTime.UtcNow
            };
            _db.ChannelFollowers.Add(follower);
            await _db.SaveChangesAsync();

            return Json(new { success = true, channelId = channel.Id });
        }

        [HttpPost]
        public async Task<IActionResult> FollowChannel(int channelId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var channel = await _db.Channels.FindAsync(channelId);
            if (channel == null) return NotFound();

            var isFollowing = await _db.ChannelFollowers
                .AnyAsync(cf => cf.ChannelId == channelId && cf.UserId == currentUserId);

            if (!isFollowing)
            {
                var follower = new ChannelFollower
                {
                    ChannelId = channelId,
                    UserId = currentUserId,
                    JoinedAt = DateTime.UtcNow
                };
                _db.ChannelFollowers.Add(follower);
                await _db.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UnfollowChannel(int channelId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var follower = await _db.ChannelFollowers
                .FirstOrDefaultAsync(cf => cf.ChannelId == channelId && cf.UserId == currentUserId);

            if (follower != null)
            {
                _db.ChannelFollowers.Remove(follower);
                await _db.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetChannelMessages(int channelId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify user follows channel or owns it
            var channel = await _db.Channels.FindAsync(channelId);
            if (channel == null) return NotFound();

            var isMember = channel.OwnerId == currentUserId || await _db.ChannelFollowers
                .AnyAsync(cf => cf.ChannelId == channelId && cf.UserId == currentUserId);

            if (!isMember) return Forbid();

            var messages = await _db.ChannelMessages
                .Include(cm => cm.Sender)
                .Where(cm => cm.ChannelId == channelId)
                .OrderBy(cm => cm.SentAt)
                .Select(cm => new
                {
                    cm.Id,
                    cm.Content,
                    cm.Type,
                    cm.FileUrl,
                    SentAt = cm.SentAt.ToString("o"),
                    SenderName = cm.Sender.FullName ?? cm.Sender.Username,
                    SenderAvatar = cm.Sender.ProfilePicture ?? "/images/default-avatar.svg"
                })
                .ToListAsync();

            return Json(messages);
        }

        [HttpPost]
        public async Task<IActionResult> PostChannelMessage(int channelId, string content, string type, IFormFile? file)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var channel = await _db.Channels.FindAsync(channelId);
            if (channel == null) return NotFound();

            // Only Owner can post message (broadcast)
            if (channel.OwnerId != currentUserId) return Forbid("Only the channel owner can post messages.");

            var message = new ChannelMessage
            {
                ChannelId = channelId,
                SenderId = currentUserId,
                Type = type,
                SentAt = DateTime.UtcNow
            };

            if (type == "Text")
            {
                if (string.IsNullOrEmpty(content)) return BadRequest("Message content cannot be empty.");
                message.Content = content;
            }
            else if (file != null && file.Length > 0)
            {
                message.FileUrl = await _imageUploadService.UploadFileAsync(file, "channels");
                message.Content = file.FileName;
            }
            else
            {
                return BadRequest("No message text or file attachment provided.");
            }

            _db.ChannelMessages.Add(message);
            await _db.SaveChangesAsync();

            return Json(new { success = true, messageId = message.Id });
        }
    }
}
