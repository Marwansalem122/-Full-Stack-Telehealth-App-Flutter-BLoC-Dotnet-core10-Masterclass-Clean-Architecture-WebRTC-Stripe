using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

public class FailedLoginAttemptConfiguration : IEntityTypeConfiguration<FailedLoginAttempt>
{
    public void Configure(EntityTypeBuilder<FailedLoginAttempt> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EmailAttempted).HasMaxLength(256);
        // No FK to Users — EmailAttempted may not correspond to a real
        // account at all (that's the whole point of logging attempts).
        builder.HasIndex(a => a.AttemptedAtUtc); // for future v2 alerting queries
    }
}