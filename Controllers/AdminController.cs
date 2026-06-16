using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Models;
using System.Net;
using System.Net.Mail;

namespace Site.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private const string AdminEmail = "moin69603@gmail.com";
        private const string AdminPassword = "admin123";

        public AdminController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // ─── Auth ───────────────────────────────────────────────────
        // Admin login logic has been moved to AuthController

        public IActionResult Logout()
        {
            HttpContext.Session.Remove("IsAdmin");
            return RedirectToAction("Login", "Auth");
        }

        // ─── Guard ──────────────────────────────────────────────────
        private bool IsAdmin() => HttpContext.Session.GetString("IsAdmin") == "true";

        // ─── Dashboard ──────────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");

            ViewBag.UserCount    = await _context.Users.CountAsync();
            ViewBag.ServiceCount = await _context.Services.CountAsync();
            ViewBag.ActiveServiceCount = await _context.Services.CountAsync(s => s.IsActive);
            ViewBag.PurchaseCount = await _context.UserServices.CountAsync();
            ViewBag.PendingReports = await _context.Reports.CountAsync(r => r.Status == "Pending");
            ViewBag.RecentUsers  = await _context.Users
                .OrderByDescending(u => u.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View();
        }

        // ─── Users ──────────────────────────────────────────────────
        public async Task<IActionResult> Users()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            var users = await _context.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
            return View(users);
        }

        public async Task<IActionResult> UserDetail(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            var purchases = await _context.UserServices
                .Include(us => us.Service)
                .Where(us => us.UserId == id)
                .ToListAsync();
            ViewBag.Purchases = purchases;
            return View(user);
        }

        // ─── Moderation Actions ─────────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> WarnUser(int userId, string reason = "Violation of community guidelines")
        {
            if (!IsAdmin()) return Json(new { success = false });
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Json(new { success = false, message = "User not found" });

            user.WarningCount += 1;
            await _context.SaveChangesAsync();

            // Send warning email
            try
            {
                var template = await System.IO.File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "Mails", "Warning_Template.html"));
                template = template.Replace("{{USER_NAME}}", user.FullName ?? user.Username)
                                   .Replace("{{REASON}}", reason)
                                   .Replace("{{WARNING_COUNT}}", user.WarningCount.ToString());
                SendAdminEmail(user.Email, "⚠️ Warning Notice - SMS SITE", template);
            }
            catch { }

            return Json(new { success = true, message = $"Warning issued. Total warnings: {user.WarningCount}" });
        }

        [HttpPost]
        public async Task<IActionResult> SuspendUser(int userId, int days, string reason = "Repeated violations")
        {
            if (!IsAdmin()) return Json(new { success = false });
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Json(new { success = false, message = "User not found" });

            user.SuspendedUntil = DateTime.UtcNow.AddDays(days);
            await _context.SaveChangesAsync();

            // Send suspension email
            try
            {
                var template = await System.IO.File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "Mails", "Suspend_Template.html"));
                template = template.Replace("{{USER_NAME}}", user.FullName ?? user.Username)
                                   .Replace("{{REASON}}", reason)
                                   .Replace("{{DAYS}}", days.ToString())
                                   .Replace("{{SUSPEND_DATE}}", user.SuspendedUntil.Value.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"));
                SendAdminEmail(user.Email, $"🚫 Account Suspended for {days} Days - SMS SITE", template);
            }
            catch { }

            return Json(new { success = true, message = $"User suspended for {days} day(s)." });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(int userId, string reason = "Severe violation of community guidelines")
        {
            if (!IsAdmin()) return Json(new { success = false });
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Json(new { success = false, message = "User not found" });

            // Send deletion email before deleting
            try
            {
                var template = await System.IO.File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "Mails", "Delete_Template.html"));
                template = template.Replace("{{USER_NAME}}", user.FullName ?? user.Username)
                                   .Replace("{{REASON}}", reason);
                SendAdminEmail(user.Email, "❌ Account Permanently Deleted - SMS SITE", template);
            }
            catch { }

            try 
            {
                // Remove dependent records to satisfy Restrict/NoAction foreign key constraints
                var statusViewers = _context.StatusViewers.Where(x => x.ViewerId == userId);
                _context.StatusViewers.RemoveRange(statusViewers);

                var channelFollowers = _context.ChannelFollowers.Where(x => x.UserId == userId);
                _context.ChannelFollowers.RemoveRange(channelFollowers);

                var channelMessages = _context.ChannelMessages.Where(x => x.SenderId == userId);
                _context.ChannelMessages.RemoveRange(channelMessages);

                var reports = _context.Reports.Where(x => x.ReporterId == userId || x.ReportedUserId == userId);
                _context.Reports.RemoveRange(reports);

                var callLogs = _context.CallLogs.Where(x => x.CallerId == userId || x.ReceiverId == userId);
                _context.CallLogs.RemoveRange(callLogs);

                var contacts = _context.Contacts.Where(x => x.OwnerId == userId || x.ContactUserId == userId);
                _context.Contacts.RemoveRange(contacts);

                var messages = _context.Messages.Where(x => x.SenderId == userId);
                _context.Messages.RemoveRange(messages);

                var participants = _context.ChatParticipants.Where(x => x.UserId == userId);
                _context.ChatParticipants.RemoveRange(participants);

                var statuses = _context.Statuses.Where(x => x.UserId == userId);
                _context.Statuses.RemoveRange(statuses);

                var channels = _context.Channels.Where(x => x.OwnerId == userId);
                _context.Channels.RemoveRange(channels);

                var communities = _context.Communities.Where(x => x.OwnerId == userId);
                _context.Communities.RemoveRange(communities);
                
                var userServices = _context.UserServices.Where(x => x.UserId == userId);
                _context.UserServices.RemoveRange(userServices);

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "User permanently deleted." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // ─── Reports ────────────────────────────────────────────────
        public async Task<IActionResult> Reports()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            var reports = await _context.Reports
                .Include(r => r.Reporter)
                .Include(r => r.ReportedUser)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        [HttpPost]
        public async Task<IActionResult> MarkReportReviewed(int reportId)
        {
            if (!IsAdmin()) return Json(new { success = false });
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null) return Json(new { success = false });
            report.Status = "Reviewed";
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ─── Services CRUD ──────────────────────────────────────────
        public async Task<IActionResult> Services()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            var services = await _context.Services
                .Include(s => s.UserServices)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            return View(services);
        }

        public IActionResult CreateService()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateService(Service service)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            if (!ModelState.IsValid) return View(service);
            service.CreatedAt = DateTime.UtcNow;
            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Service created successfully!";
            return RedirectToAction("Services");
        }

        public async Task<IActionResult> EditService(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();
            return View(service);
        }

        [HttpPost]
        public async Task<IActionResult> EditService(Service service)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            if (!ModelState.IsValid) return View(service);
            _context.Services.Update(service);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Service updated!";
            return RedirectToAction("Services");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteService(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Auth");
            var service = await _context.Services.FindAsync(id);
            if (service != null)
            {
                _context.Services.Remove(service);
                await _context.SaveChangesAsync();
            }
            TempData["Success"] = "Service deleted.";
            return RedirectToAction("Services");
        }

        // ─── Email Helper ────────────────────────────────────────────
        private void SendAdminEmail(string toEmail, string subject, string htmlBody)
        {
            string smtpHost = _config["SmtpSettings:Host"] ?? "";
            int smtpPort = int.Parse(_config["SmtpSettings:Port"] ?? "587");
            string smtpUser = _config["SmtpSettings:Username"] ?? "";
            string smtpPass = _config["SmtpSettings:Password"] ?? "";
            string fromName = _config["SmtpSettings:FromName"] ?? "SMS SITE";

            using var client = new SmtpClient(smtpHost, smtpPort);
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(smtpUser, smtpPass);

            using var message = new MailMessage();
            message.From = new MailAddress(smtpUser, fromName);
            message.To.Add(toEmail);
            message.Subject = subject;
            message.Body = htmlBody;
            message.IsBodyHtml = true;
            client.Send(message);
        }
    }
}
