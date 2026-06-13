using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Models;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Site.Services;

namespace Site.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly ImageUploadService _imageUploadService;

        public AuthController(AppDbContext db, IConfiguration config, ImageUploadService imageUploadService)
        {
            _db = db;
            _config = config;
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
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(Users model)
        {
            if (model == null)
            {
                ViewBag.Error = "Invalid request.";
                return View(new Users());
            }
            // Remove check for fields we set later
            ModelState.Remove("VerificationCode");

            if (!ModelState.IsValid)
            {
                ViewBag.Error = "Please fill all fields correctly.";
                return View(model);
            }

            // Check if Username or Email already exists
            var existingUser = await _db.Users.AnyAsync(u => u.Username == model.Username || u.Email == model.Email);
            if (existingUser)
            {
                ViewBag.Error = "Username or Email already exists.";
                return View(model);
            }

            // Check if Mobile number already exists (using normalized matching)
            var allUsers = await _db.Users.ToListAsync();
            var normalizedInputPhone = NormalizePhoneNumber(model.MobileNumber);
            if (allUsers.Any(u => NormalizePhoneNumber(u.MobileNumber) == normalizedInputPhone))
            {
                ViewBag.Error = "Mobile number is already registered.";
                return View(model);
            }

            // Generate 4-digit OTP
            string otp = new Random().Next(1000, 9999).ToString();

            // Create temporary unverified user
            var user = new Users
            {
                Username = model.Username,
                Email = model.Email,
                MobileNumber = model.MobileNumber,
                Password = model.Password, // In a real production app, encrypt/hash this!
                VerificationCode = otp,
                IsVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Store in TempData/Session for next steps
            TempData["RegUserId"] = user.Id;
            TempData["RegEmail"] = user.Email;
            TempData["RegOTP"] = otp;

            try
            {
                SendEmail(user.Email, otp);
                TempData["SuccessMessage"] = "Verification code has been sent to your email. Check spam/junk folder too.";
                return RedirectToAction("RegisterVerifyOTP");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EMAIL ERROR] Failed to send OTP to {user.Email}: {ex.Message}");
                TempData["Error"] = "Could not send email OTP: " + ex.Message;
                return RedirectToAction("RegisterVerifyOTP"); // Still redirect so they can proceed
            }
        }

        [HttpGet]
        public IActionResult RegisterVerifyOTP()
        {
            if (TempData["RegEmail"] == null)
                return RedirectToAction("Register");

            TempData.Keep();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> RegisterVerifyOTP(string otp)
        {
            TempData.Keep();

            string storedOtp = TempData["RegOTP"]?.ToString();
            int? userId = TempData["RegUserId"] as int?;

            if (userId == null)
            {
                if (TempData["RegEmail"] != null)
                {
                    string email = TempData["RegEmail"].ToString();
                    var tempUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
                    userId = tempUser?.Id;
                }
            }

            if (string.IsNullOrEmpty(otp) || storedOtp != otp)
            {
                ViewBag.Error = "Invalid code. Please try again.";
                return View();
            }

            if (userId != null)
            {
                var user = await _db.Users.FindAsync(userId);
                if (user != null)
                {
                    user.IsVerified = true;
                    await _db.SaveChangesAsync();
                }
            }

            return RedirectToAction("SetPIN");
        }

        [HttpGet]
        public async Task<IActionResult> ResendRegisterOTP()
        {
            var regEmail = TempData.Peek("RegEmail");
            var regUserId = TempData.Peek("RegUserId");
            
            if (regEmail == null || regUserId == null)
                return RedirectToAction("Register");

            string email = regEmail.ToString();
            int userId = (int)regUserId;
            
            TempData.Keep();

            string newOtp = new Random().Next(1000, 9999).ToString();
            TempData["RegOTP"] = newOtp;

            var user = await _db.Users.FindAsync(userId);
            if (user != null)
            {
                user.VerificationCode = newOtp;
                _db.Users.Update(user);
                await _db.SaveChangesAsync();
            }

            try
            {
                SendEmail(email, newOtp);
                TempData["SuccessMessage"] = "A new verification code has been sent to your email. Please also check your spam/junk folder.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EMAIL ERROR] Resend OTP failed for {email}: {ex.Message}");
                TempData["Error"] = "Failed to send new code: " + ex.Message;
            }

            return RedirectToAction("RegisterVerifyOTP");
        }

        [HttpGet]
        public IActionResult SetPin()
        {
            if (TempData["RegEmail"] == null)
                return RedirectToAction("Register");

            TempData.Keep();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SetPin(string pin, string skip)
        {
            TempData.Keep();
            int? userId = TempData["RegUserId"] as int?;

            if (userId == null && TempData["RegEmail"] != null)
            {
                var u = await _db.Users.FirstOrDefaultAsync(x => x.Email == TempData["RegEmail"].ToString());
                userId = u?.Id;
            }

            if (userId != null)
            {
                var user = await _db.Users.FindAsync(userId);
                if (user != null && skip != "true" && !string.IsNullOrEmpty(pin))
                {
                    user.Pin = pin;
                    await _db.SaveChangesAsync();
                }
            }

            return RedirectToAction("SetFingerprint");
        }

        [HttpGet]
        public IActionResult SetFingerprint()
        {
            if (TempData["RegEmail"] == null)
                return RedirectToAction("Register");

            TempData.Keep();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SetFingerprint(string skip)
        {
            TempData.Keep();
            int? userId = TempData["RegUserId"] as int?;

            if (userId == null && TempData["RegEmail"] != null)
            {
                var u = await _db.Users.FirstOrDefaultAsync(x => x.Email == TempData["RegEmail"].ToString());
                userId = u?.Id;
            }

            if (userId != null)
            {
                var user = await _db.Users.FindAsync(userId);
                if (user != null)
                {
                    user.FingerprintEnabled = (skip != "true");
                    await _db.SaveChangesAsync();
                }
            }

            return RedirectToAction("OnboardProfile");
        }

        [HttpGet]
        public IActionResult OnboardProfile()
        {
            if (TempData["RegEmail"] == null)
                return RedirectToAction("Register");

            TempData.Keep();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> OnboardProfile(string fullName, string nickname, string dob, string gender, IFormFile? profilePic)
        {
            TempData.Keep();
            int? userId = TempData["RegUserId"] as int?;

            if (userId == null && TempData["RegEmail"] != null)
            {
                var u = await _db.Users.FirstOrDefaultAsync(x => x.Email == TempData["RegEmail"].ToString());
                userId = u?.Id;
            }

            if (userId == null)
            {
                return RedirectToAction("Register");
            }

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                return RedirectToAction("Register");
            }

            user.FullName = fullName;
            user.Nickname = nickname;
            if (DateTime.TryParse(dob, out DateTime dobVal))
            {
                user.DateOfBirth = dobVal;
            }
            user.Gender = gender;

            // Handle Profile Photo Upload
            if (profilePic != null && profilePic.Length > 0)
            {
                user.ProfilePicture = await _imageUploadService.UploadFileAsync(profilePic, "profiles");
            }
            else
            {
                // Fallback avatar
                user.ProfilePicture = "/images/default-avatar.svg";
            }

            await _db.SaveChangesAsync();

            // Clear TempData registration session
            TempData.Remove("RegUsername");
            TempData.Remove("RegEmail");
            TempData.Remove("RegPassword");
            TempData.Remove("RegMobile");
            TempData.Remove("RegOTP");
            TempData.Remove("RegPin");

            // Store final UserId for congratulations login
            TempData["VerifiedUserId"] = user.Id;

            return RedirectToAction("Congratulations");
        }

        [HttpGet]
        public async Task<IActionResult> Congratulations()
        {
            int? userId = TempData["VerifiedUserId"] as int?;
            if (userId != null)
            {
                var user = await _db.Users.FindAsync(userId);
                if (user != null)
                {
                    // Sign in the user automatically
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Email, user.Email)
                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
                }
            }
            return View();
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(Users model)
        {
            if (model == null)
            {
                ViewBag.Error = "Invalid request.";
                return View(new Users());
            }

            // Check if it's the admin logging in
            if ((model.Username == "moin69603@gmail.com" || model.Email == "moin69603@gmail.com") && model.Password == "admin123")
            {
                HttpContext.Session.SetString("IsAdmin", "true");
                return RedirectToAction("Dashboard", "Admin");
            }

            // Remove properties not used in simple Login model validation
            ModelState.Remove("ConfirmPassword");
            ModelState.Remove("Email");
            ModelState.Remove("MobileNumber");
            ModelState.Remove("VerificationCode");

            if (string.IsNullOrEmpty(model.Username) || string.IsNullOrEmpty(model.Password))
            {
                ViewBag.Error = "Please enter both Username/Email and Password.";
                return View(model);
            }

            // Find user by Username, Email, or MobileNumber
            var user = await _db.Users.FirstOrDefaultAsync(u => 
                (u.Username == model.Username || u.Email == model.Username || u.MobileNumber == model.Username)
                && u.Password == model.Password); // Hashing should be checked here in production

            if (user == null)
            {
                ViewBag.Error = "Invalid username/email or password.";
                return View(model);
            }

            if (!user.IsVerified)
            {
                // Send new OTP
                string otp = new Random().Next(1000, 9999).ToString();
                user.VerificationCode = otp;
                await _db.SaveChangesAsync();

                TempData["RegUserId"] = user.Id;
                TempData["RegEmail"] = user.Email;
                TempData["RegOTP"] = otp;

                try { SendEmail(user.Email, otp); } catch { }

                ViewBag.Error = "Your account is not verified yet. An OTP has been sent to your email.";
                return RedirectToAction("RegisterVerifyOTP");
            }

            // Sign in
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return RedirectToAction("Index", "Chat");
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ViewBag.Error = "Please enter your email.";
                return View();
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Error = "No account found with this email.";
                return View();
            }

            // Generate 6-digit OTP for password reset
            string otp = new Random().Next(100000, 999999).ToString();
            user.VerificationCode = otp;
            await _db.SaveChangesAsync();

            TempData["ResetEmail"] = email;
            TempData["ResetOTP"] = otp;

            try
            {
                SendEmail(email, otp);
                return RedirectToAction("VerifyOTP");
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Error sending email: " + ex.Message;
                return View();
            }
        }

        [HttpGet]
        public IActionResult VerifyOTP()
        {
            if (TempData["ResetEmail"] == null)
                return RedirectToAction("ForgotPassword");

            TempData.Keep();
            return View();
        }

        [HttpPost]
        public IActionResult VerifyOTP(string otp)
        {
            TempData.Keep();
            string storedOtp = TempData["ResetOTP"]?.ToString();

            if (string.IsNullOrEmpty(otp) || storedOtp != otp)
            {
                ViewBag.Error = "Invalid OTP. Please try again.";
                return View();
            }

            return RedirectToAction("ResetPassword");
        }

        [HttpGet]
        public IActionResult ResendLoginOTP()
        {
            if (TempData["ResetEmail"] == null)
                return RedirectToAction("ForgotPassword");

            string email = TempData["ResetEmail"].ToString();
            TempData.Keep();

            string newOtp = new Random().Next(1000, 9999).ToString();
            TempData["ResetOTP"] = newOtp;

            try
            {
                SendEmail(email, newOtp);
                TempData["SuccessMessage"] = "A new verification code has been sent to your email.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Failed to send new code: " + ex.Message;
            }

            return RedirectToAction("VerifyOTP");
        }

        [HttpGet]
        public IActionResult ResetPassword()
        {
            if (TempData["ResetEmail"] == null)
                return RedirectToAction("ForgotPassword");

            TempData.Keep();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string newPassword, string confirmPassword)
        {
            TempData.Keep();

            if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            string email = TempData["ResetEmail"]?.ToString();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                user.Password = newPassword; // Hashing should be checked here in production
                await _db.SaveChangesAsync();
            }

            TempData.Remove("ResetEmail");
            TempData.Remove("ResetOTP");

            return RedirectToAction("Login");
        }

        private void SendEmail(string toEmail, string otp)
        {
            string smtpHost = _config["SmtpSettings:Host"];
            int smtpPort = int.Parse(_config["SmtpSettings:Port"]);
            string smtpUser = _config["SmtpSettings:Username"];
            string smtpPass = _config["SmtpSettings:Password"];
            string senderEmail = _config["SmtpSettings:SenderEmail"] ?? smtpUser;
            string senderName = _config["SmtpSettings:SenderName"] ?? "HiChat Secure";

            string templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Mails", "OTP_Template.html");
            string emailBody;

            if (System.IO.File.Exists(templatePath))
            {
                emailBody = System.IO.File.ReadAllText(templatePath);
                emailBody = emailBody.Replace("{{OTP_CODE}}", otp);
            }
            else
            {
                emailBody = $"<h1>Your OTP Code</h1><p>Your verification code is: <strong>{otp}</strong></p>";
            }

            // Log OTP to server console for easy debugging/fallback in development
            Console.WriteLine("==================================================");
            Console.WriteLine($"[DEBUG OTP] Verification code for {toEmail} is: {otp}");
            Console.WriteLine("==================================================");

            using (System.Net.Mail.SmtpClient client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort))
            {
                client.Credentials = new System.Net.NetworkCredential(smtpUser, smtpPass);
                client.EnableSsl = true;

                System.Net.Mail.MailMessage mailMessage = new System.Net.Mail.MailMessage();
                // Set sender as verified sender email from settings
                mailMessage.From = new System.Net.Mail.MailAddress(senderEmail, senderName);
                mailMessage.To.Add(toEmail);
                mailMessage.Subject = "HiChat Verification Code";
                mailMessage.IsBodyHtml = true;
                mailMessage.Body = emailBody;

                client.Send(mailMessage);
            }
        }
    }
}
