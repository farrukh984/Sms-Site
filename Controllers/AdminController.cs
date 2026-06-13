using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Models;

namespace Site.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private const string AdminEmail = "moin69603@gmail.com";
        private const string AdminPassword = "admin123";

        public AdminController(AppDbContext context)
        {
            _context = context;
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
    }
}
