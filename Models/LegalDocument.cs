namespace HanaMedia.Models;

public class LegalDocument
{
    public int Id { get; set; }
    public string Kind { get; set; } = "booking";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentOriginalName { get; set; }
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public DateTime? SentAt { get; set; }
    public int? SentByUserId { get; set; }
    public static string KindLabel(string kind) => kind switch
    {
        "booking" => "Hợp đồng Booking",
        "employment" => "Hợp đồng lao động",
        "policy" => "Nội quy công ty",
        _ => "Không xác định"
    };
}
