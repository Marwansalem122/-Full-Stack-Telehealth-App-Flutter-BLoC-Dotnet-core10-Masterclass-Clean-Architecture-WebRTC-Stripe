using Microsoft.EntityFrameworkCore;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence;

public class TelehealthDbContext(DbContextOptions<TelehealthDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ConsultantProfile> ConsultantProfiles => Set<ConsultantProfile>();
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AISummary> AISummaries => Set<AISummary>();
    public DbSet<AIJob> AIJobs => Set<AIJob>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TelehealthDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}