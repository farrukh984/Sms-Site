using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Site.Data;
using Site.Models;

namespace Site.Controllers
{
    public class ServicesController : Controller
    {
        private readonly AppDbContext _context;

        public ServicesController(AppDbContext context)
        {
            _context = context;
        }

        // GET /Services — Checklist page
        public async Task<IActionResult> Index()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Auth");

            var services = await _context.Services.Where(s => s.IsActive).ToListAsync();
            var alreadyOwned = await _context.UserServices
                .Where(us => us.UserId == user.Id)
                .Select(us => us.ServiceId)
                .ToListAsync();

            ViewBag.AlreadyOwned = alreadyOwned;
            return View(services);
        }

        // GET /Services/Checkout?ids=1,2,3
        public async Task<IActionResult> Checkout(string ids)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");

            if (string.IsNullOrEmpty(ids))
                return RedirectToAction("Index");

            var idList = ids.Split(',').Select(int.Parse).ToList();
            var services = await _context.Services
                .Where(s => idList.Contains(s.Id) && s.IsActive)
                .ToListAsync();

            ViewBag.SelectedIds = ids;
            return View(services);
        }

        // POST /Services/Pay
        [HttpPost]
        public async Task<IActionResult> Pay(string ids)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return Json(new { success = false, message = "Not logged in" });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return Json(new { success = false, message = "User not found" });

            if (string.IsNullOrEmpty(ids))
                return Json(new { success = false, message = "No services selected" });

            var idList = ids.Split(',').Select(int.Parse).ToList();

            // Remove already owned to avoid duplicates
            var owned = await _context.UserServices
                .Where(us => us.UserId == user.Id && idList.Contains(us.ServiceId))
                .Select(us => us.ServiceId)
                .ToListAsync();

            var toAdd = idList.Except(owned).ToList();

            foreach (var svcId in toAdd)
            {
                var svc = await _context.Services.FindAsync(svcId);
                if (svc == null) continue;
                _context.UserServices.Add(new UserService
                {
                    UserId = user.Id,
                    ServiceId = svcId,
                    PurchasedAt = DateTime.UtcNow,
                    AmountPaid = svc.Price
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // GET /Services/MyServices
        public async Task<IActionResult> MyServices()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return RedirectToAction("Login", "Auth");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return RedirectToAction("Login", "Auth");

            var purchased = await _context.UserServices
                .Include(us => us.Service)
                .Where(us => us.UserId == user.Id)
                .ToListAsync();

            return View(purchased);
        }
    }
}
