using Microsoft.EntityFrameworkCore;
using Site.Models;

namespace Site.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Users> Users { get; set; }
        public DbSet<Chat> Chats { get; set; }
        public DbSet<ChatParticipant> ChatParticipants { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Status> Statuses { get; set; }
        public DbSet<StatusViewer> StatusViewers { get; set; }
        public DbSet<Channel> Channels { get; set; }
        public DbSet<ChannelFollower> ChannelFollowers { get; set; }
        public DbSet<ChannelMessage> ChannelMessages { get; set; }
        public DbSet<Community> Communities { get; set; }
        public DbSet<CommunityGroup> CommunityGroups { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<UserService> UserServices { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<CallLog> CallLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Relationships
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatParticipant>()
                .HasOne(cp => cp.Chat)
                .WithMany(c => c.Participants)
                .HasForeignKey(cp => cp.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            // Status Relationships
            modelBuilder.Entity<Status>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StatusViewer>()
                .HasOne(sv => sv.Status)
                .WithMany(s => s.StatusViewers)
                .HasForeignKey(sv => sv.StatusId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StatusViewer>()
                .HasOne(sv => sv.Viewer)
                .WithMany()
                .HasForeignKey(sv => sv.ViewerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Channel Relationships
            modelBuilder.Entity<Channel>()
                .HasOne(c => c.Owner)
                .WithMany()
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChannelFollower>()
                .HasOne(cf => cf.Channel)
                .WithMany(c => c.Followers)
                .HasForeignKey(cf => cf.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChannelFollower>()
                .HasOne(cf => cf.User)
                .WithMany()
                .HasForeignKey(cf => cf.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChannelMessage>()
                .HasOne(cm => cm.Channel)
                .WithMany(c => c.Messages)
                .HasForeignKey(cm => cm.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChannelMessage>()
                .HasOne(cm => cm.Sender)
                .WithMany()
                .HasForeignKey(cm => cm.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Community Relationships
            modelBuilder.Entity<Community>()
                .HasOne(c => c.Owner)
                .WithMany()
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CommunityGroup>()
                .HasOne(cg => cg.Community)
                .WithMany(c => c.CommunityGroups)
                .HasForeignKey(cg => cg.CommunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CommunityGroup>()
                .HasOne(cg => cg.Chat)
                .WithMany()
                .HasForeignKey(cg => cg.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            // Contact Relationships
            modelBuilder.Entity<Contact>()
                .HasOne(c => c.Owner)
                .WithMany()
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Service Relationships
            modelBuilder.Entity<UserService>()
                .HasOne(us => us.User)
                .WithMany()
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserService>()
                .HasOne(us => us.Service)
                .WithMany(s => s.UserServices)
                .HasForeignKey(us => us.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Report Relationships
            modelBuilder.Entity<Report>()
                .HasOne(r => r.Reporter)
                .WithMany()
                .HasForeignKey(r => r.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Report>()
                .HasOne(r => r.ReportedUser)
                .WithMany()
                .HasForeignKey(r => r.ReportedUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // CallLog Relationships
            modelBuilder.Entity<CallLog>()
                .HasOne(cl => cl.Caller)
                .WithMany()
                .HasForeignKey(cl => cl.CallerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CallLog>()
                .HasOne(cl => cl.Receiver)
                .WithMany()
                .HasForeignKey(cl => cl.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}