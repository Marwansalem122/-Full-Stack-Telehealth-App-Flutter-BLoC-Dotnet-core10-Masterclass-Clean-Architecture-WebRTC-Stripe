using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(n => n.DataJson).HasColumnType("nvarchar(max)");

        // Powers GET /notifications (list own, filter unread, paginated, sorted by newest)
        builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc });
    }
}