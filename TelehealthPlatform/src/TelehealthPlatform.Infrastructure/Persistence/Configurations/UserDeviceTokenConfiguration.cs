using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

public class UserDeviceTokenConfiguration : IEntityTypeConfiguration<UserDeviceToken>
{
    public void Configure(EntityTypeBuilder<UserDeviceToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Token)
            .IsRequired()
            .HasMaxLength(500); // FCM tokens can be long — 500 is a safe ceiling

        builder.HasIndex(t => t.Token).IsUnique();

        builder.Property(t => t.Platform)
            .HasConversion<string>()
            .HasMaxLength(10);

        // Supports the staleness-cleanup query (LastUsedAt < Now - 3 months)
        builder.HasIndex(t => t.LastUsedAtUtc);

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}