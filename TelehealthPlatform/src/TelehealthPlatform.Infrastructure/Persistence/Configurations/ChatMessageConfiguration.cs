using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// TODO before running for real: replace PlaintextPassthroughConverter with
/// a real AES converter — this stub does NOT encrypt anything, it just
/// makes the project compile with the right shape.
/// </summary>
public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MessageType)
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(2000)
            .HasConversion(new PlaintextPassthroughConverter());

        builder.HasIndex(m => m.AppointmentId);
    }
}

internal class PlaintextPassthroughConverter
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string, string>
{
    public PlaintextPassthroughConverter() : base(v => v, v => v) { }
}