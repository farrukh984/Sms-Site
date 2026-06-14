using Microsoft.EntityFrameworkCore;
using Site.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
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

            // Add Authentication (Cookie-based + Google + Facebook)
            builder.Services.AddAuthentication(options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/Auth/Login";
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
            })
            .AddGoogle(options =>
            {
                options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "dummy_client_id";
                options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "dummy_client_secret";
            })
            .AddFacebook(options =>
            {
                options.AppId = builder.Configuration["Authentication:Facebook:AppId"] ?? "dummy_app_id";
                options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"] ?? "dummy_app_secret";
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

            // Fix HTTPS detection behind Render's reverse proxy (fixes redirect_uri_mismatch)
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            // app.UseHttpsRedirection();
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
