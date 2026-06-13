using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Site.Models;
using Site.Data;
using System.Threading.Tasks;

using Site.Services;

namespace Site.Controllers
{
    public class SettingsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ImageUploadService _imageUploadService;

        public SettingsController(AppDbContext context, ImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }

        public async Task<IActionResult> Index()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Auth");
            return View(user);
        }

        public IActionResult Account() => View();

        public async Task<IActionResult> Edit()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Auth");
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(
            string name, 
            string about,
            string phone,
            string email,
            string gender,
            string dob,
            string address,
            string maritalStatus,
            string hobbies,
            string likes,
            string dislikes,
            string cuisines,
            string sports,
            string qualification,
            string school,
            string college,
            string workStatus,
            string organization,
            string designation)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Json(new { success = false, message = "Not authenticated." });
            
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return Json(new { success = false, message = "User not found." });

            user.FullName = name;
            user.About = about;
            user.MobileNumber = phone;
            user.Email = email;
            user.Gender = gender;
            
            if (System.DateTime.TryParse(dob, out var parsedDob))
            {
                user.DateOfBirth = System.DateTime.SpecifyKind(parsedDob, System.DateTimeKind.Utc);
            }
            else
            {
                user.DateOfBirth = null;
            }

            user.Address = address;
            user.MaritalStatus = maritalStatus;
            user.Hobbies = hobbies;
            user.Likes = likes;
            user.Dislikes = dislikes;
            user.Cuisines = cuisines;
            user.Sports = sports;
            user.Qualification = qualification;
            user.School = school;
            user.College = college;
            user.WorkStatus = workStatus;
            user.Organization = organization;
            user.Designation = designation;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                name = user.FullName, 
                about = user.About,
                phone = user.MobileNumber,
                email = user.Email,
                gender = user.Gender,
                dob = user.DateOfBirth?.ToString("yyyy-MM-dd"),
                address = user.Address,
                maritalStatus = user.MaritalStatus,
                hobbies = user.Hobbies,
                likes = user.Likes,
                dislikes = user.Dislikes,
                cuisines = user.Cuisines,
                sports = user.Sports,
                qualification = user.Qualification,
                school = user.School,
                college = user.College,
                workStatus = user.WorkStatus,
                organization = user.Organization,
                designation = user.Designation,
                profilePicture = user.ProfilePicture
            });
        }

        [HttpPost]
        public async Task<IActionResult> UploadAvatar(Microsoft.AspNetCore.Http.IFormFile avatarFile)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Json(new { success = false, message = "Not authenticated." });
            
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return Json(new { success = false, message = "User not found." });

            if (avatarFile != null && avatarFile.Length > 0)
            {
                try
                {
                    var fileUrl = await _imageUploadService.UploadFileAsync(avatarFile, "avatars");

                    user.ProfilePicture = fileUrl;
                    _context.Users.Update(user);
                    await _context.SaveChangesAsync();

                    return Json(new { success = true, profilePicture = user.ProfilePicture });
                }
                catch (System.Exception ex)
                {
                    return Json(new { success = false, message = ex.Message });
                }
            }

            return Json(new { success = false, message = "No file was selected." });
        }

        public IActionResult Security() => View();
        public IActionResult AccountInfo() => View();
        public async Task<IActionResult> Privacy()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Auth");
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> SavePrivacy([FromBody] PrivacyDto dto)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Json(new { success = false });
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return Json(new { success = false });

            user.LastSeenPrivacy        = dto.LastSeenPrivacy ?? "everyone";
            user.ProfilePicPrivacy      = dto.ProfilePicPrivacy ?? "everyone";
            user.AboutPrivacy           = dto.AboutPrivacy ?? "contacts";
            user.StatusPrivacy          = dto.StatusPrivacy ?? "contacts";
            user.GroupsPrivacy          = dto.GroupsPrivacy ?? "everyone";
            user.ReadReceipts           = dto.ReadReceipts;
            user.BlockUnknownMessages   = dto.BlockUnknownMessages;
            user.DisableLinkPreviews    = dto.DisableLinkPreviews;
            user.DisappearingTimer      = dto.DisappearingTimer ?? "off";

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        public async Task<IActionResult> Chats()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Auth");
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> SaveChats([FromBody] ChatSettingsDto dto)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Json(new { success = false });
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return Json(new { success = false });

            user.Theme = dto.Theme ?? "system";
            user.Wallpaper = dto.Wallpaper ?? "default";
            user.MediaUploadQuality = dto.MediaUploadQuality ?? "standard";
            user.MediaAutoDownload = dto.MediaAutoDownload ?? "wifi";
            user.SpellCheck = dto.SpellCheck;
            user.ReplaceTextWithEmoji = dto.ReplaceTextWithEmoji;
            user.EnterIsSend = dto.EnterIsSend;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        public IActionResult Notifications() => View();
        public IActionResult Help() => View();
    }
}
