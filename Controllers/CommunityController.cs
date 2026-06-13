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
    public class CommunityController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ImageUploadService _imageUploadService;

        public CommunityController(AppDbContext db, ImageUploadService imageUploadService)
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
        public async Task<IActionResult> GetMyCommunities()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Find all communities. A user sees a community if they own it,
            // OR if they are a participant in ANY group chat linked to the community.
            var userGroupChatIds = await _db.ChatParticipants
                .Where(cp => cp.UserId == currentUserId)
                .Select(cp => cp.ChatId)
                .ToListAsync();

            var communities = await _db.Communities
                .Where(c => c.OwnerId == currentUserId || 
                            c.CommunityGroups.Any(cg => userGroupChatIds.Contains(cg.ChatId)))
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    Avatar = c.Avatar ?? "/images/group-avatar.png",
                    c.OwnerId,
                    IsOwner = c.OwnerId == currentUserId,
                    Groups = c.CommunityGroups.Select(cg => new
                    {
                        cg.Id,
                        cg.ChatId,
                        cg.IsAnnouncement,
                        ChatName = cg.IsAnnouncement ? (c.Name + " Announcements") : cg.Chat.ChatName,
                        LastMessage = _db.Messages
                            .Where(m => m.ChatId == cg.ChatId)
                            .OrderByDescending(m => m.SentAt)
                            .Select(m => new
                            {
                                m.Content,
                                m.SentAt,
                                m.Type
                            })
                            .FirstOrDefault()
                    }).ToList()
                })
                .ToListAsync();

            var formatted = communities.Select(c => new
            {
                c.Id,
                c.Name,
                c.Description,
                c.Avatar,
                c.OwnerId,
                c.IsOwner,
                Groups = c.Groups.Select(g => new
                {
                    g.Id,
                    g.ChatId,
                    g.IsAnnouncement,
                    g.ChatName,
                    LastMessageText = g.LastMessage != null 
                        ? (g.LastMessage.Type == MessageType.Text ? g.LastMessage.Content : $"[Attachment: {g.LastMessage.Type}]") 
                        : "No announcements/messages yet",
                    LastMessageTime = g.LastMessage?.SentAt.ToString("o")
                }).ToList()
            }).ToList();

            return Json(formatted);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCommunity(string name, string? description, IFormFile? avatar)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            if (string.IsNullOrEmpty(name)) return BadRequest("Community name is required.");

            // 1. Create Community
            var community = new Community
            {
                Name = name,
                Description = description,
                OwnerId = currentUserId,
                CreatedAt = DateTime.UtcNow
            };

            if (avatar != null && avatar.Length > 0)
            {
                community.Avatar = await _imageUploadService.UploadFileAsync(avatar, "communities");
            }

            _db.Communities.Add(community);
            await _db.SaveChangesAsync();

            // 2. Create Announcement Group (Chat)
            var announcementChat = new Chat
            {
                IsGroup = true,
                ChatName = name + " Announcements",
                CreatedAt = DateTime.UtcNow
            };
            _db.Chats.Add(announcementChat);
            await _db.SaveChangesAsync();

            // Add Owner as participant in announcement chat
            var participant = new ChatParticipant
            {
                ChatId = announcementChat.Id,
                UserId = currentUserId
            };
            _db.ChatParticipants.Add(participant);
            await _db.SaveChangesAsync();

            // 3. Link Announcement Chat as CommunityGroup
            var communityGroup = new CommunityGroup
            {
                CommunityId = community.Id,
                ChatId = announcementChat.Id,
                IsAnnouncement = true
            };
            _db.CommunityGroups.Add(communityGroup);
            await _db.SaveChangesAsync();

            return Json(new { success = true, communityId = community.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableGroupsToLink()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Find all Group Chats that the user participates in and are NOT already linked to any community
            var userGroupChatIds = await _db.ChatParticipants
                .Where(cp => cp.UserId == currentUserId && cp.Chat.IsGroup)
                .Select(cp => cp.ChatId)
                .ToListAsync();

            var linkedChatIds = await _db.CommunityGroups
                .Select(cg => cg.ChatId)
                .ToListAsync();

            var availableGroups = await _db.Chats
                .Where(c => userGroupChatIds.Contains(c.Id) && !linkedChatIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.ChatName
                })
                .ToListAsync();

            return Json(availableGroups);
        }

        [HttpPost]
        public async Task<IActionResult> AddGroupToCommunity(int communityId, int chatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify owner
            var community = await _db.Communities.FindAsync(communityId);
            if (community == null) return NotFound();
            if (community.OwnerId != currentUserId) return Forbid();

            // Check if group is already linked
            var isAlreadyLinked = await _db.CommunityGroups
                .AnyAsync(cg => cg.ChatId == chatId);

            if (isAlreadyLinked) return BadRequest("This group is already linked to a community.");

            var newLink = new CommunityGroup
            {
                CommunityId = communityId,
                ChatId = chatId,
                IsAnnouncement = false
            };
            _db.CommunityGroups.Add(newLink);
            
            // Wait, when adding a group to community, the users in the group should automatically get access to the announcements chat.
            // Let's add all participants of the new group to the community announcements group if they are not already in it.
            var announcementsChatId = await _db.CommunityGroups
                .Where(cg => cg.CommunityId == communityId && cg.IsAnnouncement)
                .Select(cg => cg.ChatId)
                .FirstOrDefaultAsync();

            if (announcementsChatId != 0)
            {
                var groupUserIds = await _db.ChatParticipants
                    .Where(cp => cp.ChatId == chatId)
                    .Select(cp => cp.UserId)
                    .ToListAsync();

                var existingAnnouncementUserIds = await _db.ChatParticipants
                    .Where(cp => cp.ChatId == announcementsChatId)
                    .Select(cp => cp.UserId)
                    .ToListAsync();

                var usersToAdd = groupUserIds.Except(existingAnnouncementUserIds).ToList();
                foreach (var uid in usersToAdd)
                {
                    _db.ChatParticipants.Add(new ChatParticipant
                    {
                        ChatId = announcementsChatId,
                        UserId = uid
                    });
                }
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }
    }
}
