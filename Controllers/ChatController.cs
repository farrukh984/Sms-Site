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
    public class ChatController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ImageUploadService _imageUploadService;

        public ChatController(AppDbContext db, ImageUploadService imageUploadService)
        {
            _db = db;
            _imageUploadService = imageUploadService;
        }

        private string NormalizePhoneNumber(string number)
        {
            if (string.IsNullOrEmpty(number)) return string.Empty;
            var digits = new string(number.Where(char.IsDigit).ToArray());
            if (digits.Length >= 10)
            {
                return digits.Substring(digits.Length - 10);
            }
            return digits;
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
        public async Task<IActionResult> GetChats()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Load all chats this user participates in
            var participantChats = await _db.ChatParticipants
                .Where(cp => cp.UserId == currentUserId)
                .Select(cp => cp.ChatId)
                .ToListAsync();

            var chats = await _db.Chats
                .Where(c => participantChats.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.IsGroup,
                    c.CreatedAt,
                    c.GroupPicture,
                    ChatName = c.IsGroup ? c.ChatName : null,
                    // Get the other participant for Direct Chats
                    OtherUser = _db.ChatParticipants
                        .Where(cp => cp.ChatId == c.Id && cp.UserId != currentUserId)
                        .Select(cp => new
                        {
                            cp.User.Id,
                            cp.User.FullName,
                            cp.User.Nickname,
                            cp.User.ProfilePicture,
                            cp.User.MobileNumber
                        })
                        .FirstOrDefault(),
                    // Get the last message in this chat
                    LastMessage = _db.Messages
                        .Where(m => m.ChatId == c.Id)
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => new
                        {
                            m.Content,
                            m.SentAt,
                            m.Type,
                            m.SenderId
                        })
                        .FirstOrDefault(),
                    // Count unread messages
                    UnreadCount = _db.Messages
                        .Count(m => m.ChatId == c.Id && m.SenderId != currentUserId && !m.IsRead)
                })
                .ToListAsync();

            // Load current user's contacts for display name mapping
            var myContacts = await _db.Contacts
                .Where(con => con.OwnerId == currentUserId)
                .ToListAsync();

            // Format for display
            var formattedChats = chats.Select(c =>
            {
                string displayName = "Unknown User";
                if (c.IsGroup)
                {
                    displayName = c.ChatName;
                }
                else if (c.OtherUser != null)
                {
                    var normalizedOther = NormalizePhoneNumber(c.OtherUser.MobileNumber);
                    var matchedContact = myContacts.FirstOrDefault(con => NormalizePhoneNumber(con.ContactNumber) == normalizedOther);
                    if (matchedContact != null)
                    {
                        displayName = $"{matchedContact.FirstName} {matchedContact.LastName}".Trim();
                    }
                    else
                    {
                        displayName = c.OtherUser.FullName ?? c.OtherUser.Nickname ?? c.OtherUser.MobileNumber ?? "Unknown User";
                    }
                }

                return new
                {
                    c.Id,
                    c.IsGroup,
                    Name = displayName,
                    Avatar = c.IsGroup
                        ? (string.IsNullOrEmpty(c.GroupPicture) ? "/images/group-avatar.png" : c.GroupPicture)
                        : (c.OtherUser?.ProfilePicture ?? "/images/default-avatar.svg"),
                    LastMessageText = c.LastMessage != null 
                        ? (c.LastMessage.Type == MessageType.Text ? c.LastMessage.Content : $"[Attachment: {c.LastMessage.Type}]") 
                        : "No messages yet",
                    LastMessageTime = c.LastMessage != null 
                        ? c.LastMessage.SentAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") 
                        : c.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    UnreadCount = c.UnreadCount,
                    OtherUserId = c.OtherUser?.Id
                };
            }).OrderByDescending(c => c.LastMessageTime).ToList();

            return Json(formattedChats);
        }

        [HttpGet]
        public async Task<IActionResult> GetChatHistory(int chatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Check if user is a participant
            var isParticipant = await _db.ChatParticipants.AnyAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (!isParticipant) return Forbid();

            // Load messages
            var messages = await _db.Messages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.SentAt)
                .Select(m => new
                {
                    m.Id,
                    m.ChatId,
                    m.SenderId,
                    SenderName = m.Sender.FullName ?? m.Sender.Username,
                    m.Content,
                    Type = m.Type.ToString(),
                    m.FileUrl,
                    SentAt = m.SentAt.ToString("o"),
                    m.IsRead,
                    m.ReplyToMessageId,
                    ReplyToMessageContent = m.ReplyToMessage != null ? m.ReplyToMessage.Content : null,
                    m.IsDeleted,
                    m.Reactions
                })
                .ToListAsync();

            // Mark incoming messages as read
            var unreadMessages = await _db.Messages
                .Where(m => m.ChatId == chatId && m.SenderId != currentUserId && !m.IsRead)
                .ToListAsync();

            if (unreadMessages.Any())
            {
                unreadMessages.ForEach(m => m.IsRead = true);
                await _db.SaveChangesAsync();
            }

            return Json(messages);
        }

        [HttpGet]
        public async Task<IActionResult> GetUserDetails(int userId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();
            return Json(new {
                username = user.FullName ?? user.Username,
                avatar = user.ProfilePicture ?? "/images/default-avatar.svg"
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetContacts()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Get all contacts of the current user
            var myContacts = await _db.Contacts
                .Where(c => c.OwnerId == currentUserId)
                .ToListAsync();

            var verifiedUsers = await _db.Users
                .Where(u => u.Id != currentUserId && u.IsVerified)
                .ToListAsync();

            var contactsList = new List<object>();

            foreach (var contact in myContacts)
            {
                var normalizedContactPhone = NormalizePhoneNumber(contact.ContactNumber);
                var matchedUser = verifiedUsers.FirstOrDefault(u => NormalizePhoneNumber(u.MobileNumber) == normalizedContactPhone);

                if (matchedUser != null)
                {
                    contactsList.Add(new
                    {
                        matchedUser.Id,
                        matchedUser.Username,
                        FullName = $"{contact.FirstName} {contact.LastName}".Trim(),
                        matchedUser.Nickname,
                        ProfilePicture = matchedUser.ProfilePicture ?? "/images/default-avatar.svg"
                    });
                }
            }

            var sortedContacts = contactsList
                .OrderBy(c => ((dynamic)c).FullName)
                .ToList();

            return Json(sortedContacts);
        }

        [HttpPost]
        public async Task<IActionResult> StartChat(int targetUserId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify target user exists
            var targetUser = await _db.Users.FindAsync(targetUserId);
            if (targetUser == null) return NotFound();

            // Check if a direct chat already exists between these two users
            var existingChatId = await _db.ChatParticipants
                .Where(cp => cp.UserId == currentUserId)
                .Select(cp => cp.ChatId)
                .FirstOrDefaultAsync(chatId => _db.ChatParticipants.Any(cp => cp.ChatId == chatId && cp.UserId == targetUserId));

            if (existingChatId != 0)
            {
                var existingChat = await _db.Chats.FindAsync(existingChatId);
                if (existingChat != null && !existingChat.IsGroup)
                {
                    return Json(new { chatId = existingChatId });
                }
            }

            // Create new Direct Chat
            var chat = new Chat
            {
                IsGroup = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.Chats.Add(chat);
            await _db.SaveChangesAsync();

            // Add participants
            var participant1 = new ChatParticipant { ChatId = chat.Id, UserId = currentUserId };
            var participant2 = new ChatParticipant { ChatId = chat.Id, UserId = targetUserId };
            _db.ChatParticipants.AddRange(participant1, participant2);
            await _db.SaveChangesAsync();

            return Json(new { chatId = chat.Id });
        }

        [HttpPost]
        public async Task<IActionResult> UploadAttachment(int chatId, string type, IFormFile file, int? replyToMessageId, string? caption)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            if (file == null || file.Length == 0) return BadRequest("File is empty.");

            // Verify participant
            var isParticipant = await _db.ChatParticipants.AnyAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (!isParticipant) return Forbid();

            // ── 5-Message Limit for Direct Chats ──
            var chat = await _db.Chats.FindAsync(chatId);
            if (chat != null && !chat.IsGroup)
            {
                var limitResult = await CheckDirectChatLimit(chatId, currentUserId);
                if (limitResult.IsLimited)
                    return Json(new { success = false, limited = true, message = limitResult.Message, sentCount = limitResult.SentCount });
            }

            // Save File using ImageUploadService
            var fileUrl = await _imageUploadService.UploadFileAsync(file, "attachments");

            // Map message type enum
            MessageType msgType = MessageType.Text;
            if (Enum.TryParse(type, true, out MessageType parsedType))
            {
                msgType = parsedType;
            }

            // Save Message to DB
            var message = new Message
            {
                ChatId = chatId,
                SenderId = currentUserId,
                Content = !string.IsNullOrWhiteSpace(caption) ? caption : file.FileName, // Store caption or file name
                Type = msgType,
                FileUrl = fileUrl,
                SentAt = DateTime.UtcNow,
                IsRead = false,
                ReplyToMessageId = replyToMessageId
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            // Fetch reply content if any
            string? replyContent = null;
            if (replyToMessageId.HasValue)
            {
                var repMsg = await _db.Messages.FindAsync(replyToMessageId.Value);
                if (repMsg != null) replyContent = repMsg.Content;
            }

            return Json(new { fileUrl = fileUrl, messageId = message.Id, fileName = file.FileName, replyToMessageContent = replyContent });
        }

        [HttpPost]
        public async Task<IActionResult> SaveTextMessage(int chatId, string content, int? replyToMessageId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify participant
            var isParticipant = await _db.ChatParticipants.AnyAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (!isParticipant) return Forbid();

            // ── 5-Message Limit for Direct Chats ──
            var chat = await _db.Chats.FindAsync(chatId);
            if (chat != null && !chat.IsGroup)
            {
                var limitResult = await CheckDirectChatLimit(chatId, currentUserId);
                if (limitResult.IsLimited)
                    return Json(new { success = false, limited = true, message = limitResult.Message, sentCount = limitResult.SentCount });
            }

            var message = new Message
            {
                ChatId = chatId,
                SenderId = currentUserId,
                Content = content,
                Type = MessageType.Text,
                SentAt = DateTime.UtcNow,
                IsRead = false,
                ReplyToMessageId = replyToMessageId
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            // Fetch reply content if any
            string? replyContent = null;
            if (replyToMessageId.HasValue)
            {
                var repMsg = await _db.Messages.FindAsync(replyToMessageId.Value);
                if (repMsg != null) replyContent = repMsg.Content;
            }

            return Json(new { success = true, messageId = message.Id, replyToMessageContent = replyContent });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMessage(int messageId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return Json(new { success = false, error = "Unauthorized" });

            var message = await _db.Messages.FindAsync(messageId);
            if (message == null) return Json(new { success = false, error = "Not found" });

            if (message.SenderId != currentUserId) return Json(new { success = false, error = "Cannot delete someone else's message." });

            message.IsDeleted = true;
            message.Content = "";
            message.FileUrl = null;
            
            _db.Messages.Update(message);
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        public class ForwardPayloadItem
        {
            public string content { get; set; }
            public string type { get; set; }
            public string fileUrl { get; set; }
        }

        public class ForwardRequest
        {
            public List<int> chatIds { get; set; }
            public List<ForwardPayloadItem> payloads { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> ForwardMessages([FromBody] ForwardRequest req)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            if (req == null || req.chatIds == null || req.payloads == null) return BadRequest();

            var broadcastData = new List<object>();

            foreach(var chatId in req.chatIds) {
                var isParticipant = await _db.ChatParticipants.AnyAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
                if (!isParticipant) continue;

                foreach(var p in req.payloads) {
                    MessageType msgType = MessageType.Text;
                    Enum.TryParse(p.type, true, out msgType);
                    
                    var msg = new Message {
                        ChatId = chatId,
                        SenderId = currentUserId,
                        Content = p.content ?? "",
                        Type = msgType,
                        FileUrl = p.fileUrl,
                        SentAt = DateTime.UtcNow,
                        IsRead = false
                    };
                    _db.Messages.Add(msg);
                    await _db.SaveChangesAsync(); // save immediately to get Id for broadcast

                    broadcastData.Add(new {
                        chatId = chatId,
                        messageId = msg.Id,
                        content = msg.Content,
                        type = msg.Type.ToString(),
                        fileUrl = msg.FileUrl
                    });
                }
            }

            return Json(new { success = true, broadcastData = broadcastData });
        }

        [HttpPost]
        public async Task<IActionResult> ReactToMessage(int messageId, string emoji)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var message = await _db.Messages.FindAsync(messageId);
            if (message == null) return NotFound();

            var reactions = new Dictionary<string, ReactionDetail>();
            if (!string.IsNullOrEmpty(message.Reactions))
            {
                try
                {
                    reactions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ReactionDetail>>(message.Reactions, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    try
                    {
                        var oldReactions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(message.Reactions);
                        if (oldReactions != null)
                        {
                            reactions = new Dictionary<string, ReactionDetail>();
                            foreach (var kvp in oldReactions)
                            {
                                reactions[kvp.Key] = new ReactionDetail
                                {
                                    Emoji = kvp.Value,
                                    Username = "User",
                                    UserAvatar = "/images/default-avatar.svg",
                                    ReactedAt = DateTime.UtcNow
                                };
                            }
                        }
                    }
                    catch {}
                }
            }

            if (reactions == null) reactions = new Dictionary<string, ReactionDetail>();

            if (string.IsNullOrEmpty(emoji))
            {
                reactions.Remove(currentUserId.ToString());
            }
            else
            {
                var user = await _db.Users.FindAsync(currentUserId);
                var fullName = !string.IsNullOrEmpty(user?.FullName) ? user.FullName : user?.Username ?? "User";
                var avatar = !string.IsNullOrEmpty(user?.ProfilePicture) ? user.ProfilePicture : "/images/default-avatar.svg";

                reactions[currentUserId.ToString()] = new ReactionDetail
                {
                    Emoji = emoji,
                    Username = fullName,
                    UserAvatar = avatar,
                    ReactedAt = DateTime.UtcNow
                };
            }

            message.Reactions = System.Text.Json.JsonSerializer.Serialize(reactions);
            _db.Messages.Update(message);
            await _db.SaveChangesAsync();

            return Json(new { success = true, reactions = message.Reactions });
        }

        // Helper: Check if direct chat message limit is reached
        private async Task<(bool IsLimited, string Message, int SentCount)> CheckDirectChatLimit(int chatId, int currentUserId)
        {
            const int MESSAGE_LIMIT = 5;

            // Count messages sent by this user in this chat
            var sentCount = await _db.Messages
                .CountAsync(m => m.ChatId == chatId && m.SenderId == currentUserId);

            if (sentCount < MESSAGE_LIMIT)
                return (false, string.Empty, sentCount);

            // Get current user's mobile number
            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
                return (true, "User not found.", sentCount);

            // Get the other participant in this direct chat
            var otherParticipant = await _db.ChatParticipants
                .Where(cp => cp.ChatId == chatId && cp.UserId != currentUserId)
                .Select(cp => cp.UserId)
                .FirstOrDefaultAsync();

            if (otherParticipant == 0)
                return (true, "Chat participant not found.", sentCount);

            // Check if the other user has added current user to their contacts
            // (match by mobile number — same as WhatsApp's mutual contact system)
            var otherUserContacts = await _db.Contacts
                .Where(c => c.OwnerId == otherParticipant)
                .ToListAsync();

            var otherUserAddedMe = otherUserContacts
                .Any(c => NormalizePhoneNumber(c.ContactNumber) == NormalizePhoneNumber(currentUser.MobileNumber));

            if (otherUserAddedMe)
                return (false, string.Empty, sentCount); // limit fully lifted — they added me back

            var msg = $"You can only send {MESSAGE_LIMIT} messages until the other person adds you to their contacts.";
            return (true, msg, sentCount);
        }

        [HttpGet]
        public async Task<IActionResult> GetMessageLimitStatus(int chatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var chat = await _db.Chats.FindAsync(chatId);
            if (chat == null || chat.IsGroup)
                return Json(new { limited = false, sentCount = 0, limit = 5 });

            var result = await CheckDirectChatLimit(chatId, currentUserId);
            return Json(new { limited = result.IsLimited, sentCount = result.SentCount, limit = 5, message = result.Message });
        }

        [HttpGet]
        public async Task<IActionResult> GetProfileInfo(int userId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();

            // Collect some media files sent by this user
            var userMedia = await _db.Messages
                .Where(m => m.SenderId == userId && (m.Type == MessageType.Image))
                .OrderByDescending(m => m.SentAt)
                .Select(m => m.FileUrl)
                .Take(6)
                .ToListAsync();

            // Check if current user has this user as a contact using normalized matching
            var myContacts = await _db.Contacts.Where(c => c.OwnerId == currentUserId).ToListAsync();
            var matchedContact = myContacts.FirstOrDefault(c => NormalizePhoneNumber(c.ContactNumber) == NormalizePhoneNumber(user.MobileNumber));
            var isContact = matchedContact != null;
            string displayName = isContact ? $"{matchedContact.FirstName} {matchedContact.LastName}".Trim() : (user.FullName ?? user.Username);

            return Json(new
            {
                user.Id,
                FullName = displayName,
                user.Nickname,
                user.Email,
                user.MobileNumber,
                ProfilePicture = user.ProfilePicture ?? "/images/default-avatar.svg",
                Gender = user.Gender ?? "Not Specified",
                JoinedDate = user.CreatedAt.ToString("MMMM dd, yyyy"),
                Media = userMedia,
                IsContact = isContact
            });
        }

        [HttpPost]
        public async Task<IActionResult> AddContact(string firstName, string lastName, string number)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(number))
            {
                return Json(new { success = false, message = "First Name and Contact Number are required." });
            }

            var contact = new Contact
            {
                OwnerId = currentUserId,
                FirstName = firstName.Trim(),
                LastName = (lastName ?? "").Trim(),
                ContactNumber = number.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _db.Contacts.Add(contact);
            await _db.SaveChangesAsync();

            // Find matching user using normalized comparison
            var verifiedUsers = await _db.Users.Where(u => u.IsVerified).ToListAsync();
            var normalizedInput = NormalizePhoneNumber(number);
            var targetUser = verifiedUsers.FirstOrDefault(u => NormalizePhoneNumber(u.MobileNumber) == normalizedInput);

            int? targetUserId = targetUser?.Id;
            string? targetUserName = targetUser != null ? $"{firstName} {lastName}".Trim() : null;
            string? targetUserAvatar = targetUser?.ProfilePicture ?? "/images/default-avatar.svg";

            return Json(new { success = true, message = "Contact added successfully!", targetUserId = targetUserId, name = targetUserName, avatar = targetUserAvatar });
        }

        [HttpGet]
        public async Task<IActionResult> GetPersonalContacts()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var contacts = await _db.Contacts
                .Where(c => c.OwnerId == currentUserId)
                .OrderBy(c => c.FirstName)
                .ThenBy(c => c.LastName)
                .Select(c => new
                {
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.ContactNumber,
                    FullName = c.FirstName + " " + c.LastName
                })
                .ToListAsync();

            return Json(contacts);
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroupChat(string groupName, List<int> memberIds, IFormFile? groupPicture)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            if (string.IsNullOrWhiteSpace(groupName))
            {
                return Json(new { success = false, message = "Group name is required." });
            }

            if (memberIds == null || !memberIds.Any())
            {
                return Json(new { success = false, message = "Please select at least one member to add." });
            }

            string? groupPictureUrl = null;
            if (groupPicture != null && groupPicture.Length > 0)
            {
                try
                {
                    groupPictureUrl = await _imageUploadService.UploadFileAsync(groupPicture, "groups");
                }
                catch (Exception ex)
                {
                    // Fallback to null / default
                }
            }

            // Create Group Chat
            var chat = new Chat
            {
                IsGroup = true,
                ChatName = groupName.Trim(),
                CreatedAt = DateTime.UtcNow,
                GroupPicture = groupPictureUrl,
                CreatedById = currentUserId
            };
            _db.Chats.Add(chat);
            await _db.SaveChangesAsync();

            // Add Current User as a participant and admin
            var currentParticipant = new ChatParticipant
            {
                ChatId = chat.Id,
                UserId = currentUserId,
                JoinedAt = DateTime.UtcNow,
                IsAdmin = true
            };
            _db.ChatParticipants.Add(currentParticipant);

            // Add other members as participants
            foreach (var memberId in memberIds.Distinct())
            {
                var participant = new ChatParticipant
                {
                    ChatId = chat.Id,
                    UserId = memberId,
                    JoinedAt = DateTime.UtcNow,
                    IsAdmin = false
                };
                _db.ChatParticipants.Add(participant);
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, chatId = chat.Id, chatName = chat.ChatName, groupPicture = chat.GroupPicture });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGroupDetails(int chatId, string groupName, IFormFile? groupPicture)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify chat exists and is a group
            var chat = await _db.Chats.FindAsync(chatId);
            if (chat == null || !chat.IsGroup) return NotFound();

            // Check if current user is admin of the group
            var currentParticipant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (currentParticipant == null || !currentParticipant.IsAdmin)
            {
                return Json(new { success = false, message = "Only group admins can update group details." });
            }

            if (!string.IsNullOrWhiteSpace(groupName))
            {
                chat.ChatName = groupName.Trim();
            }

            if (groupPicture != null && groupPicture.Length > 0)
            {
                try
                {
                    chat.GroupPicture = await _imageUploadService.UploadFileAsync(groupPicture, "groups");
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Failed to upload group picture: " + ex.Message });
                }
            }

            _db.Chats.Update(chat);
            await _db.SaveChangesAsync();

            return Json(new { success = true, chatId = chat.Id, chatName = chat.ChatName, groupPicture = chat.GroupPicture });
        }

        [HttpPost]
        public async Task<IActionResult> AddGroupMembers(int chatId, List<int> memberIds)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var chat = await _db.Chats.FindAsync(chatId);
            if (chat == null || !chat.IsGroup) return NotFound();

            var currentParticipant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (currentParticipant == null || !currentParticipant.IsAdmin)
            {
                return Json(new { success = false, message = "Only group admins can add new members." });
            }

            if (memberIds == null || !memberIds.Any())
            {
                return Json(new { success = false, message = "No members selected to add." });
            }

            var existingMemberIds = await _db.ChatParticipants
                .Where(cp => cp.ChatId == chatId)
                .Select(cp => cp.UserId)
                .ToListAsync();

            var addedCount = 0;
            foreach (var memberId in memberIds.Distinct())
            {
                if (!existingMemberIds.Contains(memberId))
                {
                    var participant = new ChatParticipant
                    {
                        ChatId = chatId,
                        UserId = memberId,
                        JoinedAt = DateTime.UtcNow,
                        IsAdmin = false
                    };
                    _db.ChatParticipants.Add(participant);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                await _db.SaveChangesAsync();
            }

            return Json(new { success = true, addedCount = addedCount });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleAdminStatus(int chatId, int targetUserId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var chat = await _db.Chats.FindAsync(chatId);
            if (chat == null || !chat.IsGroup) return NotFound();

            // Verify current user is admin
            var currentParticipant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (currentParticipant == null || !currentParticipant.IsAdmin)
            {
                return Json(new { success = false, message = "Only group admins can modify admin statuses." });
            }

            // Get target participant
            var targetParticipant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == targetUserId);
            if (targetParticipant == null)
            {
                return Json(new { success = false, message = "Selected user is not in this group." });
            }

            // Toggle admin flag
            targetParticipant.IsAdmin = !targetParticipant.IsAdmin;
            _db.ChatParticipants.Update(targetParticipant);
            await _db.SaveChangesAsync();

            return Json(new { success = true, isAdmin = targetParticipant.IsAdmin });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveGroupMember(int chatId, int targetUserId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var chat = await _db.Chats.FindAsync(chatId);
            if (chat == null || !chat.IsGroup) return NotFound();

            // Verify current user is admin
            var currentParticipant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (currentParticipant == null || !currentParticipant.IsAdmin)
            {
                return Json(new { success = false, message = "Only group admins can remove members." });
            }

            // Get target participant
            var targetParticipant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == targetUserId);
            if (targetParticipant == null)
            {
                return Json(new { success = false, message = "Selected user is not in this group." });
            }

            if (targetUserId == currentUserId)
            {
                 return Json(new { success = false, message = "You cannot remove yourself using this action." });
            }

            // Remove participant
            _db.ChatParticipants.Remove(targetParticipant);
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ClearChat(int chatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify user is a participant
            var isParticipant = await _db.ChatParticipants.AnyAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (!isParticipant) return Forbid();

            // Delete all messages in this chat
            var messages = await _db.Messages.Where(m => m.ChatId == chatId).ToListAsync();
            _db.Messages.RemoveRange(messages);
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ExitGroup(int chatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            var chat = await _db.Chats.FindAsync(chatId);
            if (chat == null || !chat.IsGroup) return NotFound();

            // Get current user participant record
            var participant = await _db.ChatParticipants
                .FirstOrDefaultAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (participant == null)
                return Json(new { success = false, message = "You are not a member of this group." });

            // If this user is the only admin, we must either block exit or assign another admin
            if (participant.IsAdmin)
            {
                var otherAdmins = await _db.ChatParticipants
                    .Where(cp => cp.ChatId == chatId && cp.UserId != currentUserId && cp.IsAdmin)
                    .CountAsync();

                if (otherAdmins == 0)
                {
                    // Try to promote the next oldest member to admin
                    var nextMember = await _db.ChatParticipants
                        .Where(cp => cp.ChatId == chatId && cp.UserId != currentUserId)
                        .OrderBy(cp => cp.JoinedAt)
                        .FirstOrDefaultAsync();

                    if (nextMember != null)
                    {
                        nextMember.IsAdmin = true;
                        _db.ChatParticipants.Update(nextMember);
                    }
                }
            }

            _db.ChatParticipants.Remove(participant);
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupInfo(int chatId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int currentUserId)) return BadRequest();

            // Verify the user is a participant of the group
            var isParticipant = await _db.ChatParticipants.AnyAsync(cp => cp.ChatId == chatId && cp.UserId == currentUserId);
            if (!isParticipant) return Forbid();

            var chat = await _db.Chats
                .Include(c => c.Participants)
                .ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(c => c.Id == chatId);

            if (chat == null || !chat.IsGroup) return NotFound();

            var currentUserParticipant = chat.Participants.FirstOrDefault(cp => cp.UserId == currentUserId);
            var currentUserIsAdmin = currentUserParticipant?.IsAdmin ?? false;

            var members = chat.Participants.Select(cp => new
            {
                cp.User.Id,
                FullName = cp.User.FullName ?? cp.User.Username,
                cp.User.Username,
                cp.User.Nickname,
                ProfilePicture = cp.User.ProfilePicture ?? "/images/default-avatar.svg",
                MobileNumber = cp.User.MobileNumber,
                IsAdmin = cp.IsAdmin
            }).ToList();

            string creatorName = "Admin";
            if (chat.CreatedById.HasValue)
            {
                var creatorUser = await _db.Users.FindAsync(chat.CreatedById.Value);
                if (creatorUser != null)
                {
                    creatorName = creatorUser.FullName ?? creatorUser.Username;
                }
            }

            return Json(new
            {
                chatId = chat.Id,
                groupName = chat.ChatName,
                groupPicture = chat.GroupPicture ?? "/images/group-avatar.png",
                createdAt = chat.CreatedAt.ToString("MMMM dd, yyyy"),
                createdBy = creatorName,
                memberCount = members.Count,
                members = members,
                currentUserIsAdmin = currentUserIsAdmin
            });
        }

        // ─── Report User ─────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ReportUser(int reportedUserId, string reason)
        {
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            if (currentUserId == null) return Json(new { success = false, message = "Not logged in" });
            if (string.IsNullOrWhiteSpace(reason)) return Json(new { success = false, message = "Please provide a reason" });
            if (reportedUserId == currentUserId) return Json(new { success = false, message = "You cannot report yourself" });

            var reportedUser = await _db.Users.FindAsync(reportedUserId);
            if (reportedUser == null) return Json(new { success = false, message = "User not found" });

            var report = new Site.Models.Report
            {
                ReporterId = currentUserId.Value,
                ReportedUserId = reportedUserId,
                Reason = reason,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _db.Reports.Add(report);
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Report submitted. Our team will review it shortly." });
        }
    }

    public class ReactionDetail
    {
        public string Emoji { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string UserAvatar { get; set; } = string.Empty;
        public DateTime ReactedAt { get; set; } = DateTime.UtcNow;
    }
}
