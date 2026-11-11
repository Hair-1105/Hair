using HanaMedia.Constants;

namespace HanaMedia.ViewModels;

public sealed class BookingStaffViewModel
{
    public bool HasEmployeeProfile { get; init; }
    public IReadOnlyList<BookingStaffRow> Bookings { get; init; } = [];
}

public sealed class BookingStaffRow
{
    public int Id { get; init; }
    public string ClientName { get; init; } = "";
    public string CampaignName { get; init; } = "";
    public List<string> KolNames { get; init; } = [];
    public string? LegacyKolName { get; init; }
    public DateOnly Deadline { get; init; }
    public decimal AllocatedWage { get; init; }
    public string? Status { get; init; }
    public string? ContractStatus { get; init; }
    public string? PostLink { get; init; }
    public string StatusLabel => Status switch
    {
        "dang_cho" => "Chờ", "da_duyet" => "Giám đốc duyệt", "da_nghiem_thu" => "Đã nghiệm thu",
        "dang_trien_khai" => "Đang chạy", "hoan_thanh" => "Hoàn thành", "huy" => "Đã hủy",
        _ => "Chưa cập nhật"
    };
    public string ContractLabel => ContractWorkflow.Label(ContractStatus);
    public bool HasSafePostLink => Uri.TryCreate(PostLink, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
