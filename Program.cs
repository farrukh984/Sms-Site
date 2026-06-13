using Microsoft.EntityFrameworkCore;
using Site.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Site.Hubs;
using System;
using Site.Services;

namespace Site
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add DbContext with PostgreSQL connection
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Add services to the container.
            builder.Services.AddSingleton<ImageUploadService>();
            builder.Services.AddControllersWithViews();

            // Add SignalR
            builder.Services.AddSignalR();

            // Add Authentication (Cookie-based)
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Auth/Login";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                });

            // Add Session for TempData and storage
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(60);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseDeveloperExceptionPage();

            // app.UseHttpsRedirection(); // Disabled for Somee free hosting
            app.UseRouting();

            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseStaticFiles();
            app.MapStaticAssets();
            
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // Map SignalR Hub
            app.MapHub<ChatHub>("/chatHub");

            // Seed default services if none exist
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Site.Data.AppDbContext>();
                try {
                    // Auto-create database tables if they don't exist
                    db.Database.Migrate();
                    
                    if (!db.Services.Any())
                    {
                        db.Services.AddRange(
                            new Site.Models.Service { Title = "Premium Chat Themes",    Description = "Unlock 6 beautiful chat themes for your interface.",  Price = 4.99m,  IconClass = "fas fa-palette", IsActive = true },
                            new Site.Models.Service { Title = "Custom Bubble Colors",   Description = "Personalize your outgoing chat bubble colors.",        Price = 9.99m,  IconClass = "fas fa-comment", IsActive = true },
                            new Site.Models.Service { Title = "HD Chat Wallpapers",     Description = "Set stunning wallpapers as your chat background.",      Price = 14.99m, IconClass = "fas fa-image",   IsActive = true }
                        );
                        db.SaveChanges();
                    }
                } catch { /* Tables may not exist yet */ }
            }

            app.Run();
        }
    }
}
