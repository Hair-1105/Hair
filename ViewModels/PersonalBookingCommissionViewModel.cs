namespace HanaMedia.ViewModels;

public sealed class PersonalBookingCommissionViewModel
{
    public bool HasEmployeeProfile { get; init; }
    public int BookingCount { get; init; }
    public int CustomerPaidCount { get; init; }
    public int LegacyBookingCount { get; init; }
    public decimal Total { get; init; }
    public decimal PendingCustomerPayment { get; init; }
    public decimal CompanyPaid { get; init; }
    public decimal CompanyUnpaid => Total - CompanyPaid;
}
