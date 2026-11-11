using System.ComponentModel.DataAnnotations;

namespace HanaMedia.ViewModels;

public class LegalDocumentEditViewModel
{
    public int Id { get; set; }
    [Required, RegularExpression("^(booking|employment|policy)$")]
    public string Kind { get; set; } = "booking";
    [Required(ErrorMessage = "Nhập tiêu đề hồ sơ."), StringLength(200)]
    public string Title { get; set; } = "";
    [StringLength(100000)]
    public string? Content { get; set; }
    public int? BookingId { get; set; }
    public int? EmployeeId { get; set; }
    public int Revision { get; set; }
    public IFormFile? Attachment { get; set; }
}
