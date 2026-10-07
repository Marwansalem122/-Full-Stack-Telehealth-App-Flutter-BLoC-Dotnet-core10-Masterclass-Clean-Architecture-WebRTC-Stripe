using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;
using TelehealthPlatform.Domain.Enums;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(a => a.IdempotencyKey)
            .IsUnique();

        var activeStatusFilter =
            $"[Status] NOT IN ('{AppointmentStatus.Cancelled}', '{AppointmentStatus.NoShow}', '{AppointmentStatus.PaymentFailed}')";

        builder.HasIndex(a => new { a.ConsultantId, a.ScheduledStartUtc })
            .IsUnique()
            .HasFilter(activeStatusFilter);

        builder.HasIndex(a => new { a.PatientId, a.ScheduledStartUtc })
            .IsUnique()
            .HasFilter(activeStatusFilter);

        builder.HasOne<Consultation>(a => a.Consultation)
            .WithOne(c => c.Appointment)
            .HasForeignKey<Consultation>(c => c.AppointmentId);

        builder.HasOne(a => a.Payment)
            .WithOne(p => p.Appointment)
            .HasForeignKey<Payment>(p => p.AppointmentId);
    }
}