using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

public class StripeWebhookEventConfiguration : IEntityTypeConfiguration<StripeWebhookEvent>
{
    public void Configure(EntityTypeBuilder<StripeWebhookEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.StripeEventId)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(e => e.StripeEventId)
            .IsUnique();
    }
}