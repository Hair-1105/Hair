using Microsoft.EntityFrameworkCore;

namespace HanaMedia.Models;

public partial class ApplicationDbContext
{
    public DbSet<BookingKol> BookingKols => Set<BookingKol>();
    public DbSet<BookingPayment> BookingPayments => Set<BookingPayment>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegalDocument>(b =>
        {
            b.ToTable("legal_documents", t => t.HasCheckConstraint("CK_legal_document_kind",
                "([Kind] = 'booking' AND [BookingId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([Kind] = 'employment' AND [EmployeeId] IS NOT NULL AND [BookingId] IS NULL) OR ([Kind] = 'policy' AND [BookingId] IS NULL AND [EmployeeId] IS NULL)"));
            b.Property(x => x.Kind).HasMaxLength(20);
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Content).HasMaxLength(100000);
            b.Property(x => x.AttachmentName).HasMaxLength(100);
            b.Property(x => x.AttachmentOriginalName).HasMaxLength(255);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Campaign>().Property(x => x.ConfirmedAt).IsConcurrencyToken();
        modelBuilder.Entity<Campaign>().Property(x => x.Status).IsConcurrencyToken();
        modelBuilder.Entity<Booking>(b =>
        {
            b.Property(x => x.CompanyPercent).HasPrecision(5, 2);
            b.Property(x => x.CommissionPercent).HasPrecision(5, 2);
            b.Property(x => x.CastPercent).HasPrecision(5, 2);
            b.Property(x => x.ContractStatus).IsConcurrencyToken();
            b.Property(x => x.UpdatedAt).IsConcurrencyToken();
            b.Property(x => x.LegalFeedback).HasMaxLength(2000);
        });
        modelBuilder.Entity<BookingKol>(b =>
        {
            b.ToTable("booking_kols");
            b.HasKey(x => new { x.BookingId, x.KolId });
            b.Property(x => x.CastAmount).HasPrecision(18, 2);
            b.Property(x => x.AllocationAmount).HasPrecision(18, 2);
            b.HasOne(x => x.Booking).WithMany(x => x.BookingKols).HasForeignKey(x => x.BookingId);
            b.HasOne(x => x.Kol).WithMany().HasForeignKey(x => x.KolId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BookingPayment>(b =>
        {
            b.ToTable("booking_payments");
            b.HasKey(x => new { x.BookingId, x.Kind, x.PayeeId });
            b.Property(x => x.Kind).HasMaxLength(20);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.UpdatedAt).IsConcurrencyToken();
            b.HasOne(x => x.Booking).WithMany(x => x.Payments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Idea>().HasOne(x => x.PrimaryKol).WithMany().HasForeignKey(x => x.PrimaryKolId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Idea>().HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
    }
}
