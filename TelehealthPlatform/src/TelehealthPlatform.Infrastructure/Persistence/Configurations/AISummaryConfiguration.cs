using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelehealthPlatform.Domain.Entities;

namespace TelehealthPlatform.Infrastructure.Persistence.Configurations;

public class AISummaryConfiguration : IEntityTypeConfiguration<AISummary>
{
    public void Configure(EntityTypeBuilder<AISummary> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.ConsultationId)
            .IsUnique();

        builder.Property(s => s.StructuredOutputJson)
            .HasColumnType("nvarchar(max)");

        builder.HasOne(s => s.Consultation)
            .WithOne(c => c.AISummary)
            .HasForeignKey<AISummary>(s => s.ConsultationId);
    }
}