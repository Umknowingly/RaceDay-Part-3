using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Models;

namespace RaceDay.Api.Data;

/// <summary>
/// EF Core Code-First context following the submitted Part 1 ERD: User, Participant, Event,
/// Category, Enrolment and Result. Additions needed by the endpoint plan and the Part 2 brief
/// are listed in the README. Every foreign key is NO ACTION (DeleteBehavior.Restrict) so
/// enrolment and result history is never cascade-deleted.
/// </summary>
public class RaceDayDbContext : DbContext
{
    public RaceDayDbContext(DbContextOptions<RaceDayDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Enrolment> Enrolments => Set<Enrolment>();
    public DbSet<Result> Results => Set<Result>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("User", t => t.HasCheckConstraint("CK_User_Role", "[Role] IN ('Organiser', 'Participant')"));
            e.HasKey(x => x.UserID);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Email).IsRequired().HasMaxLength(150);
            e.Property(x => x.PasswordHash).IsRequired().HasMaxLength(255);
            e.Property(x => x.Role).IsRequired().HasMaxLength(20);
            e.Property(x => x.ContactNumber).HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UQ_User_Email");
        });

        b.Entity<Participant>(e =>
        {
            e.ToTable("Participant");
            e.HasKey(x => x.ParticipantID);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.UserID).IsUnique().HasDatabaseName("UQ_Participant_User");
            e.HasOne(x => x.User).WithOne(u => u.Participant)
                .HasForeignKey<Participant>(x => x.UserID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Participant_User");
        });

        b.Entity<Event>(e =>
        {
            e.ToTable("Event", t =>
            {
                t.HasCheckConstraint("CK_Event_EventType", "[EventType] IN ('Run', 'Walk', 'Cycle')");
                t.HasCheckConstraint("CK_Event_DistanceKm", "[DistanceKm] > 0");
            });
            e.HasKey(x => x.EventID);
            e.Property(x => x.Name).IsRequired().HasMaxLength(150);
            e.Property(x => x.Venue).IsRequired().HasMaxLength(150);
            e.Property(x => x.DistanceKm).HasPrecision(6, 2);
            e.Property(x => x.EventType).IsRequired().HasMaxLength(10);
            e.HasOne(x => x.Organiser).WithMany(u => u.OrganisedEvents)
                .HasForeignKey(x => x.UserID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Event_User");
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("Category", t =>
            {
                t.HasCheckConstraint("CK_Category_DistanceKm", "[DistanceKm] IS NULL OR [DistanceKm] > 0");
                t.HasCheckConstraint("CK_Category_MaxParticipants", "[MaxParticipants] > 0");
            });
            e.HasKey(x => x.CategoryID);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.DistanceKm).HasPrecision(6, 2);
            e.Property(x => x.MaxParticipants).HasDefaultValue(100);
            e.HasIndex(x => new { x.EventID, x.Name }).IsUnique().HasDatabaseName("UQ_Category_Event_Name");
            e.HasOne(x => x.Event).WithMany(ev => ev.Categories)
                .HasForeignKey(x => x.EventID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Category_Event");
            e.HasOne(x => x.Creator).WithMany(u => u.CreatedCategories)
                .HasForeignKey(x => x.UserID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Category_User");
        });

        b.Entity<Enrolment>(e =>
        {
            e.ToTable("Enrolment", t =>
                t.HasCheckConstraint("CK_Enrolment_Status", "[Status] IN ('Pending', 'Confirmed', 'Cancelled')"));
            e.HasKey(x => x.EnrolmentID);
            e.Property(x => x.EnrolDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.Status).IsRequired().HasMaxLength(20).HasDefaultValue(EnrolmentStatuses.Pending);
            e.Property(x => x.EmergencyContactName).IsRequired().HasMaxLength(100);
            e.Property(x => x.EmergencyContactNumber).IsRequired().HasMaxLength(20);

            // A participant enters an event once, choosing one of its categories.
            e.HasIndex(x => new { x.ParticipantID, x.EventID }).IsUnique().HasDatabaseName("UQ_Enrolment_Participant_Event");

            e.HasOne(x => x.Participant).WithMany(p => p.Enrolments)
                .HasForeignKey(x => x.ParticipantID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Enrolment_Participant");
            e.HasOne(x => x.Event).WithMany(ev => ev.Enrolments)
                .HasForeignKey(x => x.EventID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Enrolment_Event");
            e.HasOne(x => x.Category).WithMany(c => c.Enrolments)
                .HasForeignKey(x => x.CategoryID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Enrolment_Category");
        });

        b.Entity<Result>(e =>
        {
            e.ToTable("Result", t =>
            {
                t.HasCheckConstraint("CK_Result_Status", "[Status] IN ('Finished', 'DNF', 'DSQ')");
                t.HasCheckConstraint("CK_Result_Position", "[Position] IS NULL OR [Position] > 0");
            });
            e.HasKey(x => x.ResultID);
            // SQLite has no TIME type. Storing the finish time as text ("00:52:30") keeps it sortable.
            e.Property(x => x.FinishTime).HasColumnName("Time").HasConversion<string>();
            e.Property(x => x.Status).IsRequired().HasMaxLength(20).HasDefaultValue(ResultStatuses.Finished);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.EnrolmentID).IsUnique().HasDatabaseName("UQ_Result_Enrolment");
            e.HasOne(x => x.Enrolment).WithOne(en => en.Result)
                .HasForeignKey<Result>(x => x.EnrolmentID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Result_Enrolment");
        });
    }
}
