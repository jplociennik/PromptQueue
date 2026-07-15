using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptQueue.Domain.Prompts;

namespace PromptQueue.Infrastructure.Persistence.Configurations;

/// <summary>Mapowanie encji Prompt na tabelę prompts; Id nadawane przez aplikację, status jako tekst.</summary>
public class PromptConfiguration : IEntityTypeConfiguration<Prompt>
{
    public void Configure(EntityTypeBuilder<Prompt> builder)
    {
        builder.ToTable("prompts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Content).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
